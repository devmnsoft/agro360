using System.Security.Cryptography;
using System.Text;
using Agro360.Application;
using Agro360.Application.Abstractions;
using Agro360.Application.Contracts;
using Agro360.Domain.Portal;
using Agro360.Infrastructure.Persistence;
using Agro360.Multitenancy;
using Agro360.SharedKernel;
using Dapper;
using Microsoft.Extensions.Logging;

namespace Agro360.Infrastructure.Services;

public sealed class PortalService(
    DatabaseExecutor db,
    ITenantContext tenant,
    IClock clock,
    IPasswordHasher passwords,
    ITokenService tokens,
    IPublicTraceabilityService publicTrace,
    IDocumentService documentService,
    ILogger<PortalService> logger) : IPortalService
{
    public Task<IReadOnlyList<PortalInvitationRow>> InvitationsAsync(string? status, CancellationToken ct) =>
        Tx(async (c, t) => (IReadOnlyList<PortalInvitationRow>)(await c.QueryAsync<PortalInvitationRow>(new CommandDefinition(
            """
            select i.id, i.name, i.email, p.code profile, i.entity_type entitytype, i.entity_label entitylabel,
                   case when i.revoked_at is not null then 'REVOKED'
                        when i.accepted_at is not null then 'ACCEPTED'
                        when i.expires_at <= now() then 'EXPIRED'
                        else 'PENDING' end status,
                   i.expires_at expiresat, i.created_at createdat
            from agro360.portal_invitations i
            join agro360.portal_profiles p on p.id = i.profile_id and p.tenant_id = i.tenant_id
            where i.tenant_id = @TenantId
              and (@Status is null or case when i.revoked_at is not null then 'REVOKED'
                                          when i.accepted_at is not null then 'ACCEPTED'
                                          when i.expires_at <= now() then 'EXPIRED'
                                          else 'PENDING' end = @Status)
            order by i.created_at desc
            """,
            new { tenant.TenantId, Status = status?.Trim().ToUpperInvariant() }, t, cancellationToken: ct))).ToArray(), ct);

    public Task<PortalInvitationCreated> InviteAsync(PortalInvitationCommand command, CancellationToken ct)
    {
        var email = PortalRules.Email(command.Email);
        var profile = PortalRules.Profile(command.Profile);
        if (string.IsNullOrWhiteSpace(command.Name) || command.Name.Trim().Length > 160)
            throw new DomainException("Informe o nome do convidado.", "agro360.portal_name");
        if (command.EntityId == Guid.Empty || string.IsNullOrWhiteSpace(command.EntityType))
            throw new DomainException("Selecione a entidade vinculada.", "agro360.portal_entity");
        if (command.ExpiresAt <= clock.UtcNow || command.ExpiresAt > clock.UtcNow.AddDays(30))
            throw new DomainException("A validade deve estar entre agora e 30 dias.", "agro360.portal_expiry");

        return Tx(async (c, t) =>
        {
            var raw = Microsoft.IdentityModel.Tokens.Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(48));
            var hash = Hash(raw);
            var id = Guid.CreateVersion7();
            var profileId = await c.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                "select id from agro360.portal_profiles where tenant_id = @TenantId and code = @Profile and active",
                new { tenant.TenantId, Profile = profile }, t, cancellationToken: ct))
                ?? throw new DomainException("Perfil externo ainda não foi homologado para esta organização.", "agro360.portal_profile");

            await c.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.portal_invitations(
                    id, tenant_id, profile_id, name, email, entity_type, entity_id, entity_label, token_hash, expires_at, created_by, updated_by
                ) values (
                    @Id, @TenantId, @ProfileId, @Name, @Email, @EntityType, @EntityId, @EntityLabel, @Hash, @ExpiresAt, @UserId, @UserId
                );
                insert into agro360.platform_outbox_messages(id, tenant_id, event_type, aggregate_id, payload, occurred_at)
                values (
                    gen_random_uuid(), @TenantId, 'PortalInvitationRequested', @Id,
                    jsonb_build_object('invitationId', @Id, 'email', @Email), now()
                )
                """,
                new
                {
                    Id = id,
                    tenant.TenantId,
                    ProfileId = profileId,
                    Name = command.Name.Trim(),
                    Email = email,
                    EntityType = command.EntityType.Trim().ToUpperInvariant(),
                    command.EntityId,
                    EntityLabel = command.EntityType.Trim(),
                    Hash = hash,
                    command.ExpiresAt,
                    tenant.UserId
                }, t, cancellationToken: ct));

            InfrastructureLogMessages.PortalInvitationRegistered(logger, id, tenant.TenantId);
            return new PortalInvitationCreated(id, raw, command.ExpiresAt);
        }, ct);
    }

    public Task RevokeInvitationAsync(Guid id, string reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("Informe o motivo da revogação.", "agro360.portal_revoke.reason");

        return Tx(async (c, t) =>
        {
            if (await c.ExecuteAsync(new CommandDefinition(
                """
                update agro360.portal_invitations
                set revoked_at = now(), revoked_by = @UserId, revoke_reason = @Reason, updated_at = now(), updated_by = @UserId
                where id = @Id and tenant_id = @TenantId and accepted_at is null and revoked_at is null
                """,
                new { Id = id, tenant.TenantId, tenant.UserId, Reason = reason.Trim() }, t, cancellationToken: ct)) == 0)
            {
                throw new ConflictException("Convite inexistente ou não pode mais ser revogado.");
            }
        }, ct);
    }

    public Task<PortalAuthentication> AcceptInvitationAsync(AcceptPortalInvitationCommand command, CancellationToken ct)
    {
        if (!command.AcceptTerms)
            throw new DomainException("Você precisa aceitar os termos para continuar.", "agro360.portal_terms");
        if (string.IsNullOrWhiteSpace(command.Token))
            throw new DomainException("O link de convite é inválido.", "agro360.portal_invitation.token");
        PortalRules.Password(command.Password);

        var hash = Hash(command.Token);
        return db.InSystemTransactionAsync(async (c, t) =>
        {
            var row = await c.QuerySingleOrDefaultAsync<dynamic>(new CommandDefinition(
                """
                select i.id, i.tenant_id, i.name, i.email, i.entity_type, i.entity_id, i.expires_at, i.revoked_at, i.accepted_at,
                       p.id profile_id, p.code profile
                from agro360.portal_invitations i
                join agro360.portal_profiles p on p.id = i.profile_id and p.tenant_id = i.tenant_id
                where i.token_hash = @Hash
                for update
                """,
                new { Hash = hash }, t, cancellationToken: ct))
                ?? throw new NotFoundException("Convite", Guid.Empty);

            PortalRules.Invitation(row.expires_at, clock.UtcNow, row.revoked_at, row.accepted_at);
            var userId = Guid.CreateVersion7();
            var passwordHash = passwords.Hash(command.Password);

            await c.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.portal_external_users(
                    id, tenant_id, profile_id, name, email, password_hash, status, terms_accepted_at, last_login_at, created_by, updated_by
                ) values (
                    @UserId, @TenantId, @ProfileId, @Name, @Email, @PasswordHash, 'ACTIVE', now(), now(), @UserId, @UserId
                );
                insert into agro360.portal_external_user_links(
                    id, tenant_id, external_user_id, entity_type, entity_id, created_by, updated_by
                ) values (
                    gen_random_uuid(), @TenantId, @UserId, @EntityType, @EntityId, @UserId, @UserId
                );
                insert into agro360.portal_terms_acceptances(
                    id, tenant_id, external_user_id, term_id, accepted_at, created_by, updated_by
                )
                select gen_random_uuid(), @TenantId, @UserId, id, now(), @UserId, @UserId
                from agro360.portal_terms
                where tenant_id = @TenantId and active
                order by version desc
                limit 1;
                update agro360.portal_invitations
                set accepted_at = now(), accepted_user_id = @UserId, updated_at = now(), updated_by = @UserId
                where id = @Id;
                insert into agro360.portal_external_audit_events(
                    id, tenant_id, external_user_id, event_type, metadata
                ) values (
                    gen_random_uuid(), @TenantId, @UserId, 'INVITATION_ACCEPTED', jsonb_build_object('invitationId', @Id)
                );
                """,
                new
                {
                    Id = (Guid)row.id,
                    UserId = userId,
                    TenantId = (Guid)row.tenant_id,
                    ProfileId = (Guid)row.profile_id,
                    Name = (string)row.name,
                    Email = (string)row.email,
                    PasswordHash = passwordHash,
                    EntityType = (string)row.entity_type,
                    EntityId = (Guid)row.entity_id
                }, t, cancellationToken: ct));

            return Auth((Guid)row.tenant_id, userId, (string)row.name, (string)row.email, (string)row.profile);
        }, ct);
    }

    public Task<PortalAuthentication> LoginAsync(PortalLoginCommand command, CancellationToken ct) =>
        db.InSystemTransactionAsync(async (c, t) =>
        {
            var email = PortalRules.Email(command.Email);
            var row = await c.QuerySingleOrDefaultAsync<dynamic>(new CommandDefinition(
                """
                select u.id, u.tenant_id, u.name, u.email, u.password_hash, u.status, p.code profile
                from agro360.portal_external_users u
                join agro360.portal_profiles p on p.id = u.profile_id and p.tenant_id = u.tenant_id
                join agro360.tenancy_tenants tn on tn.id = u.tenant_id
                where tn.slug = @Slug and u.email = @Email and u.status = 'ACTIVE' and u.deleted_at is null
                """,
                new { Slug = command.TenantSlug.Trim().ToLowerInvariant(), Email = email }, t, cancellationToken: ct));

            if (row is null || !passwords.Verify(command.Password, (string)row.password_hash))
                throw new ForbiddenException("E-mail, organização ou senha inválidos.");

            await c.ExecuteAsync(new CommandDefinition(
                """
                update agro360.portal_external_users set last_login_at = now(), updated_at = now() where id = @Id and tenant_id = @TenantId;
                insert into agro360.portal_external_audit_events(id, tenant_id, external_user_id, event_type, metadata)
                values (gen_random_uuid(), @TenantId, @Id, 'LOGIN', '{}'::jsonb);
                """,
                new { Id = (Guid)row.id, TenantId = (Guid)row.tenant_id }, t, cancellationToken: ct));

            return Auth((Guid)row.tenant_id, (Guid)row.id, (string)row.name, (string)row.email, (string)row.profile);
        }, ct);

    public Task ChangePasswordAsync(PortalChangePasswordCommand command, CancellationToken ct)
    {
        PortalRules.Password(command.NewPassword);
        if (string.Equals(command.CurrentPassword, command.NewPassword, StringComparison.Ordinal))
            throw new DomainException("A nova senha deve ser diferente da senha atual.", "portal.password.same");

        return Tx(async (c, t) =>
        {
            var row = await c.QuerySingleOrDefaultAsync<dynamic>(new CommandDefinition(
                "select password_hash from agro360.portal_external_users where id = @UserId and tenant_id = @TenantId and status = 'ACTIVE' and deleted_at is null",
                new { tenant.TenantId, tenant.UserId }, t, cancellationToken: ct))
                ?? throw new ForbiddenException("Acesso externo não autorizado.");

            if (!passwords.Verify(command.CurrentPassword, (string)row.password_hash))
                throw new ForbiddenException("A senha atual informada está incorreta.");

            var newHash = passwords.Hash(command.NewPassword);
            await c.ExecuteAsync(new CommandDefinition(
                """
                update agro360.portal_external_users
                set password_hash = @NewHash, updated_at = now(), updated_by = @UserId
                where id = @UserId and tenant_id = @TenantId;
                insert into agro360.portal_external_audit_events(id, tenant_id, external_user_id, event_type, metadata)
                values (gen_random_uuid(), @TenantId, @UserId, 'PASSWORD_CHANGED', '{}'::jsonb);
                """,
                new { tenant.TenantId, tenant.UserId, NewHash = newHash }, t, cancellationToken: ct));
        }, ct);
    }

    public Task<PortalDashboard> DashboardAsync(CancellationToken ct) =>
        Tx(async (c, t) =>
        {
            var user = await User(c, t, ct);
            var profile = (string)user.profile;

            var announcements = (await c.QueryAsync<PortalAnnouncement>(new CommandDefinition(
                """
                select a.id, a.title, a.summary, a.severity, a.published_at publishedat, (r.id is not null) read
                from agro360.portal_announcements a
                left join agro360.portal_announcement_reads r
                    on r.announcement_id = a.id and r.external_user_id = @UserId and r.tenant_id = a.tenant_id
                where a.tenant_id = @TenantId
                  and a.status = 'PUBLISHED'
                  and a.published_at <= now()
                  and (a.expires_at is null or a.expires_at > now())
                  and (a.audience = 'ALL' or a.audience = @Profile)
                order by a.published_at desc
                limit 8
                """,
                new { tenant.TenantId, tenant.UserId, Profile = profile }, t, cancellationToken: ct))).ToArray();

            var reqCount = await c.ExecuteScalarAsync<long>(new CommandDefinition(
                "select count(*) from agro360.portal_requests where tenant_id = @TenantId and external_user_id = @UserId and status in ('OPEN','IN_REVIEW','WAITING_RESPONSE')",
                new { tenant.TenantId, tenant.UserId }, t, cancellationToken: ct));

            var quoteCount = await c.ExecuteScalarAsync<long>(new CommandDefinition(
                "select count(*) from agro360.portal_marketplace_quote_requests where tenant_id = @TenantId and external_user_id = @UserId and status not in ('CANCELLED','CONVERTED','REJECTED')",
                new { tenant.TenantId, tenant.UserId }, t, cancellationToken: ct));

            var docCount = await c.ExecuteScalarAsync<long>(new CommandDefinition(
                """
                select count(distinct d.id)
                from agro360.documents d
                left join agro360.documents_document_links l on l.document_id = d.id and l.tenant_id = d.tenant_id and l.deleted_at is null
                left join agro360.portal_document_permissions dp on dp.document_id = d.id and dp.tenant_id = d.tenant_id and dp.deleted_at is null
                left join agro360.portal_external_user_links ul on ul.tenant_id = d.tenant_id and ul.external_user_id = @UserId
                where d.tenant_id = @TenantId
                  and d.status = 'APPROVED'
                  and d.deleted_at is null
                  and (
                      dp.external_user_id = @UserId
                      or (dp.entity_type = ul.entity_type and dp.entity_id = ul.entity_id)
                      or (l.entity_type = ul.entity_type and l.entity_id = ul.entity_id)
                      or dp.profile_code = @Profile
                  )
                """,
                new { tenant.TenantId, tenant.UserId, Profile = profile }, t, cancellationToken: ct));

            var articleCount = await c.ExecuteScalarAsync<long>(new CommandDefinition(
                "select count(*) from agro360.support_knowledge_articles where (tenant_id = @TenantId or tenant_id is null) and status = 'PUBLISHED' and deleted_at is null",
                new { tenant.TenantId }, t, cancellationToken: ct));

            var metrics = new List<PortalMetric>
            {
                new("requests", "Solicitações abertas", reqCount, "/Portal/Requests"),
                new("quotes", "Cotações ativas", quoteCount, "/Portal/Marketplace"),
                new("documents", "Documentos liberados", docCount, "/Portal/Documents"),
                new("support", "Artigos e ajuda", articleCount, "/Portal/Support")
            };

            var activities = (await c.QueryAsync<PortalActivity>(new CommandDefinition(
                """
                select event_type type, message description, created_at occurredat
                from agro360.portal_request_events
                where tenant_id = @TenantId and created_by = @UserId
                order by created_at desc
                limit 5
                """,
                new { tenant.TenantId, tenant.UserId }, t, cancellationToken: ct))).ToArray();

            return new PortalDashboard((string)user.name, profile, metrics, announcements, activities);
        }, ct);

    public Task<IReadOnlyList<MarketplaceListing>> MarketplaceAsync(MarketplaceFilter filter, CancellationToken ct) =>
        Tx(async (c, t) => (IReadOnlyList<MarketplaceListing>)(await c.QueryAsync<MarketplaceListing>(new CommandDefinition(
            """
            select l.id, l.product_name product, l.crop, l.harvest, l.region, l.unit,
                   l.available_quantity availablequantity, l.unit_price unitprice, l.commercial_terms commercialterms,
                   coalesce(array_agg(c.name) filter(where c.name is not null), '{}') certifications
            from agro360.portal_marketplace_listings l
            left join agro360.portal_marketplace_listing_certificates lc on lc.listing_id = l.id and lc.tenant_id = l.tenant_id
            left join agro360.documents_certificates c on c.id = lc.certificate_id and c.tenant_id = l.tenant_id and c.status = 'ISSUED'
            where l.tenant_id = @TenantId
              and l.status = 'AVAILABLE'
              and l.available_quantity > 0
              and (@Search is null or l.product_name ilike '%'||@Search||'%')
              and (@Crop is null or l.crop = @Crop)
              and (@Region is null or l.region = @Region)
              and (@Unit is null or l.unit = @Unit)
              and (@MaximumPrice is null or l.unit_price <= @MaximumPrice)
            group by l.id
            order by l.product_name
            """,
            new
            {
                tenant.TenantId,
                Search = Clean(filter.Search),
                Crop = Clean(filter.Crop),
                Region = Clean(filter.Region),
                Unit = Clean(filter.Unit),
                filter.MaximumPrice
            }, t, cancellationToken: ct))).ToArray(), ct);

    public Task<Guid> RequestQuoteAsync(CreateQuoteCommand command, CancellationToken ct)
    {
        PortalRules.Quote(command.Items.Select(i => (i.ListingId, i.Quantity)).ToArray());
        var email = PortalRules.Email(command.ContactEmail);

        return Tx(async (c, t) =>
        {
            await User(c, t, ct);
            var id = Guid.CreateVersion7();
            var args = new
            {
                Id = id,
                tenant.TenantId,
                tenant.UserId,
                command.ContactName,
                Email = email,
                command.Notes,
                ListingIds = command.Items.Select(i => i.ListingId).ToArray(),
                Quantities = command.Items.Select(i => i.Quantity).ToArray()
            };

            await c.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.portal_marketplace_quote_requests(
                    id, tenant_id, external_user_id, protocol, contact_name, contact_email, notes, status, created_by, updated_by
                ) values (
                    @Id, @TenantId, @UserId,
                    'COT-'||to_char(now(),'YYYYMMDD')||'-'||upper(substr(replace(@Id::text,'-',''),1,6)),
                    @ContactName, @Email, @Notes, 'REQUESTED', @UserId, @UserId
                );
                insert into agro360.portal_marketplace_quote_events(id, tenant_id, quote_request_id, event_type, notes, created_by, updated_by)
                values (gen_random_uuid(), @TenantId, @Id, 'REQUESTED', 'Cotação aberta pelo portal', @UserId, @UserId);
                insert into agro360.portal_external_audit_events(id, tenant_id, external_user_id, event_type, metadata)
                values (gen_random_uuid(), @TenantId, @UserId, 'QUOTE_REQUESTED', jsonb_build_object('quoteId', @Id));
                """,
                args, t, cancellationToken: ct));

            var inserted = await c.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.portal_marketplace_quote_request_items(
                    id, tenant_id, quote_request_id, listing_id, quantity, unit, created_by, updated_by
                )
                select gen_random_uuid(), @TenantId, @Id, command.listing_id, command.quantity, l.unit, @UserId, @UserId
                from unnest(@ListingIds::uuid[], @Quantities::numeric[]) command(listing_id, quantity)
                join agro360.portal_marketplace_listings l on l.id = command.listing_id and l.tenant_id = @TenantId and l.status = 'AVAILABLE' and l.available_quantity >= command.quantity
                """,
                args, t, cancellationToken: ct));

            if (inserted != command.Items.Count)
                throw new DomainException("Um item não está mais disponível na quantidade solicitada.", "agro360.portal_quote.availability");

            return id;
        }, ct);
    }

    public Task<IReadOnlyList<PortalQuoteDetailDto>> MyQuotesAsync(CancellationToken ct) =>
        Tx<IReadOnlyList<PortalQuoteDetailDto>>(async (c, t) =>
        {
            var quotes = (await c.QueryAsync<dynamic>(new CommandDefinition(
                """
                select q.id, q.protocol, q.status, q.notes, q.created_at createdat
                from agro360.portal_marketplace_quote_requests q
                where q.tenant_id = @TenantId and q.external_user_id = @UserId and q.deleted_at is null
                order by q.created_at desc
                limit 50
                """,
                new { tenant.TenantId, tenant.UserId }, t, cancellationToken: ct))).ToArray();

            if (quotes.Length == 0) return Array.Empty<PortalQuoteDetailDto>();

            var quoteIds = quotes.Select(q => (Guid)q.id).ToArray();
            var items = (await c.QueryAsync<dynamic>(new CommandDefinition(
                """
                select qi.quote_request_id, qi.listing_id listingid, coalesce(l.product_name, 'Produto') product,
                       qi.unit, qi.quantity, qi.offered_unit_price offeredunitprice
                from agro360.portal_marketplace_quote_request_items qi
                left join agro360.portal_marketplace_listings l on l.id = qi.listing_id and l.tenant_id = qi.tenant_id
                where qi.tenant_id = @TenantId and qi.quote_request_id = any(@QuoteIds)
                """,
                new { tenant.TenantId, QuoteIds = quoteIds }, t, cancellationToken: ct))).ToArray();

            var itemsByQuote = items.GroupBy(i => (Guid)i.quote_request_id)
                .ToDictionary(g => g.Key, g => g.Select(i => new PortalQuoteItemDto((Guid)i.listingid, (string)i.product, (string)i.unit, (decimal)i.quantity, (decimal?)i.offeredunitprice)).ToList());

            return quotes.Select(q => new PortalQuoteDetailDto(
                (Guid)q.id,
                (string)q.protocol,
                (string)q.status,
                (string?)q.notes,
                (DateTimeOffset)q.createdat,
                itemsByQuote.TryGetValue((Guid)q.id, out var list) ? list : Array.Empty<PortalQuoteItemDto>()
            )).ToArray();
        }, ct);

    public Task<IReadOnlyList<PortalRequestRow>> RequestsAsync(CancellationToken ct) =>
        Tx(async (c, t) => (IReadOnlyList<PortalRequestRow>)(await c.QueryAsync<PortalRequestRow>(new CommandDefinition(
            """
            select id, protocol, type, subject, status, priority, created_at createdat, updated_at updatedat
            from agro360.portal_requests
            where tenant_id = @TenantId and external_user_id = @UserId and deleted_at is null
            order by updated_at desc
            """,
            new { tenant.TenantId, tenant.UserId }, t, cancellationToken: ct))).ToArray(), ct);

    public Task<PortalRequestDetailDto> RequestDetailAsync(Guid id, CancellationToken ct) =>
        Tx(async (c, t) =>
        {
            var req = await c.QuerySingleOrDefaultAsync<dynamic>(new CommandDefinition(
                """
                select id, protocol, type, subject, description, status, priority, resolution, cancellation_reason cancellationreason,
                       created_at createdat, updated_at updatedat
                from agro360.portal_requests
                where id = @Id and tenant_id = @TenantId and external_user_id = @UserId and deleted_at is null
                """,
                new { Id = id, tenant.TenantId, tenant.UserId }, t, cancellationToken: ct))
                ?? throw new NotFoundException("Solicitação", id);

            var events = (await c.QueryAsync<PortalRequestEventDto>(new CommandDefinition(
                """
                select id, event_type eventtype, message, created_at createdat
                from agro360.portal_request_events
                where request_id = @Id and tenant_id = @TenantId
                order by created_at asc
                """,
                new { Id = id, tenant.TenantId }, t, cancellationToken: ct))).ToArray();

            return new PortalRequestDetailDto(
                (Guid)req.id,
                (string)req.protocol,
                (string)req.type,
                (string)req.subject,
                (string)req.description,
                (string)req.status,
                (string)req.priority,
                (string?)req.resolution,
                (string?)req.cancellationreason,
                (DateTimeOffset)req.createdat,
                (DateTimeOffset)req.updatedat,
                events);
        }, ct);

    public Task<Guid> CreateRequestAsync(PortalRequestCommand command, CancellationToken ct)
    {
        PortalRules.Request(command.Subject, command.Description);
        return Tx(async (c, t) =>
        {
            await User(c, t, ct);
            var id = Guid.CreateVersion7();
            await c.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.portal_requests(
                    id, tenant_id, external_user_id, protocol, type, subject, description, status, priority, created_by, updated_by
                ) values (
                    @Id, @TenantId, @UserId,
                    'SOL-'||to_char(now(),'YYYYMMDD')||'-'||upper(substr(replace(@Id::text,'-',''),1,6)),
                    @Type, @Subject, @Description, 'OPEN', @Priority, @UserId, @UserId
                );
                insert into agro360.portal_request_events(id, tenant_id, request_id, event_type, message, created_by, updated_by)
                values (gen_random_uuid(), @TenantId, @Id, 'CREATED', 'Solicitação aberta pelo portal', @UserId, @UserId);
                insert into agro360.platform_outbox_messages(id, tenant_id, event_type, aggregate_id, payload, occurred_at)
                values (gen_random_uuid(), @TenantId, 'PortalRequestCreated', @Id, jsonb_build_object('requestId', @Id, 'priority', @Priority), now());
                insert into agro360.portal_external_audit_events(id, tenant_id, external_user_id, event_type, metadata)
                values (gen_random_uuid(), @TenantId, @UserId, 'REQUEST_CREATED', jsonb_build_object('requestId', @Id));
                """,
                new
                {
                    Id = id,
                    tenant.TenantId,
                    tenant.UserId,
                    Type = command.Type.Trim().ToUpperInvariant(),
                    Subject = command.Subject.Trim(),
                    Description = command.Description.Trim(),
                    Priority = command.Priority.Trim().ToUpperInvariant()
                }, t, cancellationToken: ct));
            return id;
        }, ct);
    }

    public Task CancelRequestAsync(Guid id, CancelPortalRequestCommand command, CancellationToken ct)
    {
        PortalRules.CancelRequest(command.Reason);
        return Tx(async (c, t) =>
        {
            var updated = await c.ExecuteAsync(new CommandDefinition(
                """
                update agro360.portal_requests
                set status = 'CANCELLED', cancellation_reason = @Reason, updated_at = now(), updated_by = @UserId
                where id = @Id and tenant_id = @TenantId and external_user_id = @UserId and status in ('OPEN', 'IN_REVIEW', 'WAITING_RESPONSE')
                """,
                new { Id = id, tenant.TenantId, tenant.UserId, Reason = command.Reason.Trim() }, t, cancellationToken: ct));

            if (updated == 0)
                throw new ConflictException("A solicitação não pode mais ser cancelada.");

            await c.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.portal_request_events(id, tenant_id, request_id, event_type, message, created_by, updated_by)
                values (gen_random_uuid(), @TenantId, @Id, 'CANCELLED', 'Cancelada pelo solicitante: ' || @Reason, @UserId, @UserId);
                insert into agro360.portal_external_audit_events(id, tenant_id, external_user_id, event_type, metadata)
                values (gen_random_uuid(), @TenantId, @UserId, 'REQUEST_CANCELLED', jsonb_build_object('requestId', @Id, 'reason', @Reason));
                """,
                new { Id = id, tenant.TenantId, tenant.UserId, Reason = command.Reason.Trim() }, t, cancellationToken: ct));
        }, ct);
    }

    public Task ResolveRequestAsync(Guid id, ResolvePortalRequestCommand command, CancellationToken ct)
    {
        PortalRules.ResolveRequest(command.Resolution);
        return Tx(async (c, t) =>
        {
            var updated = await c.ExecuteAsync(new CommandDefinition(
                """
                update agro360.portal_requests
                set status = 'RESOLVED', resolution = @Resolution, updated_at = now(), updated_by = @UserId
                where id = @Id and tenant_id = @TenantId and status in ('OPEN', 'IN_REVIEW', 'WAITING_RESPONSE')
                """,
                new { Id = id, tenant.TenantId, tenant.UserId, Resolution = command.Resolution.Trim() }, t, cancellationToken: ct));

            if (updated == 0)
                throw new NotFoundException("Solicitação", id);

            await c.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.portal_request_events(id, tenant_id, request_id, event_type, message, created_by, updated_by)
                values (gen_random_uuid(), @TenantId, @Id, 'RESOLVED', 'Resolvida: ' || @Resolution, @UserId, @UserId);
                """,
                new { Id = id, tenant.TenantId, tenant.UserId, Resolution = command.Resolution.Trim() }, t, cancellationToken: ct));
        }, ct);
    }

    public Task RejectRequestAsync(Guid id, RejectPortalRequestCommand command, CancellationToken ct)
    {
        PortalRules.RejectRequest(command.Reason);
        return Tx(async (c, t) =>
        {
            var updated = await c.ExecuteAsync(new CommandDefinition(
                """
                update agro360.portal_requests
                set status = 'REJECTED', cancellation_reason = @Reason, updated_at = now(), updated_by = @UserId
                where id = @Id and tenant_id = @TenantId and status in ('OPEN', 'IN_REVIEW', 'WAITING_RESPONSE')
                """,
                new { Id = id, tenant.TenantId, tenant.UserId, Reason = command.Reason.Trim() }, t, cancellationToken: ct));

            if (updated == 0)
                throw new NotFoundException("Solicitação", id);

            await c.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.portal_request_events(id, tenant_id, request_id, event_type, message, created_by, updated_by)
                values (gen_random_uuid(), @TenantId, @Id, 'REJECTED', 'Rejeitada: ' || @Reason, @UserId, @UserId);
                """,
                new { Id = id, tenant.TenantId, tenant.UserId, Reason = command.Reason.Trim() }, t, cancellationToken: ct));
        }, ct);
    }

    public Task MarkAnnouncementReadAsync(Guid id, CancellationToken ct) =>
        Tx(async (c, t) =>
        {
            await c.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.portal_announcement_reads(id, tenant_id, announcement_id, external_user_id, read_at, created_by, updated_by)
                select gen_random_uuid(), @TenantId, @Id, @UserId, now(), @UserId, @UserId
                where exists(select 1 from agro360.portal_announcements where id = @Id and tenant_id = @TenantId and status = 'PUBLISHED')
                on conflict(tenant_id, announcement_id, external_user_id) do nothing
                """,
                new { Id = id, tenant.TenantId, tenant.UserId }, t, cancellationToken: ct));
        }, ct);

    public Task<IReadOnlyList<PortalDocumentItemDto>> DocumentsAsync(CancellationToken ct) =>
        Tx(async (c, t) =>
        {
            var user = await User(c, t, ct);
            var profile = (string)user.profile;

            return (IReadOnlyList<PortalDocumentItemDto>)(await c.QueryAsync<PortalDocumentItemDto>(new CommandDefinition(
                """
                select distinct d.id documentid, d.name, dt.name documenttype, l.entity_type entitytype,
                                d.created_at createdat, v.size_bytes filesize, v.mime_type mimetype
                from agro360.documents d
                join agro360.documents_document_types dt on dt.id = d.document_type_id
                join agro360.documents_document_versions v on v.document_id = d.id and v.version_number = d.current_version and v.tenant_id = d.tenant_id
                left join agro360.documents_document_links l on l.document_id = d.id and l.tenant_id = d.tenant_id and l.deleted_at is null
                left join agro360.portal_document_permissions dp on dp.document_id = d.id and dp.tenant_id = d.tenant_id and dp.deleted_at is null
                left join agro360.portal_external_user_links ul on ul.tenant_id = d.tenant_id and ul.external_user_id = @UserId
                where d.tenant_id = @TenantId
                  and d.status = 'APPROVED'
                  and d.deleted_at is null
                  and (
                      dp.external_user_id = @UserId
                      or (dp.entity_type = ul.entity_type and dp.entity_id = ul.entity_id)
                      or (l.entity_type = ul.entity_type and l.entity_id = ul.entity_id)
                      or dp.profile_code = @Profile
                  )
                order by d.created_at desc
                limit 100
                """,
                new { tenant.TenantId, tenant.UserId, Profile = profile }, t, cancellationToken: ct))).ToArray();
        }, ct);

    public Task<StoredDownload> DownloadDocumentAsync(Guid documentId, CancellationToken ct) =>
        Tx(async (c, t) =>
        {
            var user = await User(c, t, ct);
            var profile = (string)user.profile;

            var allowed = await c.ExecuteScalarAsync<bool>(new CommandDefinition(
                """
                select exists (
                    select 1
                    from agro360.documents d
                    left join agro360.documents_document_links l on l.document_id = d.id and l.tenant_id = d.tenant_id and l.deleted_at is null
                    left join agro360.portal_document_permissions dp on dp.document_id = d.id and dp.tenant_id = d.tenant_id and dp.deleted_at is null
                    left join agro360.portal_external_user_links ul on ul.tenant_id = d.tenant_id and ul.external_user_id = @UserId
                    where d.id = @DocumentId
                      and d.tenant_id = @TenantId
                      and d.status = 'APPROVED'
                      and d.deleted_at is null
                      and (
                          dp.external_user_id = @UserId
                          or (dp.entity_type = ul.entity_type and dp.entity_id = ul.entity_id)
                          or (l.entity_type = ul.entity_type and l.entity_id = ul.entity_id)
                          or dp.profile_code = @Profile
                      )
                )
                """,
                new { DocumentId = documentId, tenant.TenantId, tenant.UserId, Profile = profile }, t, cancellationToken: ct));

            if (!allowed)
                throw new ForbiddenException("Você não possui autorização para baixar este documento.");

            await c.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.portal_external_audit_events(id, tenant_id, external_user_id, event_type, entity_type, entity_id, metadata)
                values (gen_random_uuid(), @TenantId, @UserId, 'DOCUMENT_DOWNLOADED', 'DOCUMENT', @DocumentId, '{}'::jsonb)
                """,
                new { tenant.TenantId, tenant.UserId, DocumentId = documentId }, t, cancellationToken: ct));

            return await documentService.DownloadAsync(documentId, null, ct);
        }, ct);

    public Task<IReadOnlyList<PortalSupportArticleDto>> SupportArticlesAsync(string? search, CancellationToken ct) =>
        Tx(async (c, t) => (IReadOnlyList<PortalSupportArticleDto>)(await c.QueryAsync<PortalSupportArticleDto>(new CommandDefinition(
            """
            select id, title, content, coalesce(module, type) category, published_at publishedat
            from agro360.support_knowledge_articles
            where (tenant_id = @TenantId or tenant_id is null)
              and status = 'PUBLISHED'
              and deleted_at is null
              and (@Search is null or title ilike '%'||@Search||'%' or content ilike '%'||@Search||'%')
            order by published_at desc nulls last
            limit 30
            """,
            new { tenant.TenantId, Search = Clean(search) }, t, cancellationToken: ct))).ToArray(), ct);

    public Task<PublicTraceDto?> PublicTraceAsync(string publicCode, CancellationToken ct) =>
        publicTrace.GetAsync(publicCode, ct);

    private PortalAuthentication Auth(Guid tenantId, Guid userId, string name, string email, string profile)
    {
        var pair = tokens.Create(tenantId, userId, email, [Permissions.PortalAccess, $"agro360.portal_profile.{profile.ToLowerInvariant()}"], Array.Empty<string>());
        return new(tenantId, userId, name, profile, pair.AccessToken, pair.RefreshToken, pair.ExpiresAt);
    }

    private async Task<dynamic> User(Npgsql.NpgsqlConnection c, Npgsql.NpgsqlTransaction t, CancellationToken ct) =>
        await c.QuerySingleOrDefaultAsync<dynamic>(new CommandDefinition(
            "select u.name, p.code profile from agro360.portal_external_users u join agro360.portal_profiles p on p.id = u.profile_id and p.tenant_id = u.tenant_id where u.id = @UserId and u.tenant_id = @TenantId and u.status = 'ACTIVE' and u.deleted_at is null",
            new { tenant.TenantId, tenant.UserId }, t, cancellationToken: ct))
            ?? throw new ForbiddenException("Acesso externo não autorizado.");

    private Task<T> Tx<T>(Func<Npgsql.NpgsqlConnection, Npgsql.NpgsqlTransaction, Task<T>> action, CancellationToken ct) =>
        db.InTenantTransactionAsync(action, ct);

    private Task Tx(Func<Npgsql.NpgsqlConnection, Npgsql.NpgsqlTransaction, Task> action, CancellationToken ct) =>
        db.InTenantTransactionAsync(action, ct);

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
