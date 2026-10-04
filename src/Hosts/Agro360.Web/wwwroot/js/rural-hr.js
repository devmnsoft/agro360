(() => {
    'use strict';

    const root = document.querySelector('.rural-hr-shell');
    if (!root) return;

    const api = root.dataset.api || '/api/rural-hr';
    const content = document.querySelector('#hr-content');
    const feedback = document.querySelector('#hr-feedback');
    const helpBtn = document.querySelector('#toggle-hr-help');
    const helpPanel = document.querySelector('#hr-help-panel');
    const exportBtn = document.querySelector('#hr-export-csv');

    // Dialogs
    const personDialog = document.querySelector('#hr-person-dialog');
    const personForm = document.querySelector('#hr-person-form');
    const timeDialog = document.querySelector('#hr-time-dialog');
    const timeForm = document.querySelector('#hr-time-form');
    const endTimeDialog = document.querySelector('#hr-end-time-dialog');
    const endTimeForm = document.querySelector('#hr-end-time-form');
    const transportDialog = document.querySelector('#hr-transport-dialog');
    const transportForm = document.querySelector('#hr-transport-form');
    const recordDialog = document.querySelector('#hr-record-dialog');
    const recordForm = document.querySelector('#hr-record-form');
    const actionDialog = document.querySelector('#hr-action-dialog');
    const actionForm = document.querySelector('#hr-action-form');

    const routes = {
        'Painel': 'operations-board',
        'Pessoas': 'people',
        'Equipes': 'teams',
        'Jornada': 'time-entries',
        'Alocações': 'allocations',
        'Tarifas': 'tariffs',
        'Revisões': 'data-reviews',
        'Custos de Mão de Obra': 'labor-costs',
        'Treinamentos': 'trainings',
        'EPIs': 'ppe',
        'Segurança': 'safety-risks',
        'Incidentes': 'incidents',
        'Ações Corretivas': 'corrective-actions',
        'Alojamento': 'accommodations',
        'Transporte': 'transport',
        'Dashboard RH Rural/SST': 'dashboard'
    };

    let activeTab = 'Painel';
    let route = 'operations-board';
    let boardSituation = '';
    let boardPage = 1;
    const lookupsCache = {};
    const scopedKinds = new Set(['plots', 'seasons', 'work-orders', 'allocations', 'operational-resources']);
    const newKey = () => (crypto.randomUUID ? crypto.randomUUID() : `${Date.now()}-${Math.random()}`);

    const esc = (text) => {
        if (!text) return '';
        const d = document.createElement('div');
        d.textContent = text;
        return d.innerHTML;
    };

    const notify = (message, isError = false) => {
        if (!feedback) return;
        feedback.textContent = message;
        feedback.className = `feedback-container ${isError ? 'error' : 'success'}`;
        if (!isError) {
            setTimeout(() => {
                if (feedback.textContent === message) {
                    feedback.textContent = '';
                    feedback.className = 'feedback-container';
                }
            }, 6000);
        }
    };

    const request = async (path, options = {}) => {
        const token = localStorage.getItem('agro360.accessToken');
        const url = path.startsWith('http') || path.startsWith('/api') ? path : `${api}/${path}`;
        const response = await fetch(url, {
            ...options,
            headers: {
                'Content-Type': 'application/json',
                Authorization: `Bearer ${token}`,
                ...options.headers
            }
        });

        if (!response.ok) {
            let errorText = 'Não foi possível concluir a operação.';
            try {
                const json = await response.json();
                errorText = json.detail || json.title || json.message || JSON.stringify(json);
            } catch {
                errorText = (await response.text()) || errorText;
            }
            throw new Error(errorText);
        }

        if (response.status === 204) return null;
        const ct = response.headers.get('content-type') || '';
        return ct.includes('application/json') ? response.json() : response.text();
    };

    const loadLookup = async (kind, scopeId) => {
        const key = scopeId ? `${kind}@${scopeId}` : kind;
        if (lookupsCache[key]) return lookupsCache[key];
        try {
            const qs = scopeId ? `?scopeId=${encodeURIComponent(scopeId)}` : '';
            const data = await request(`lookups/${kind}${qs}`);
            lookupsCache[key] = Array.isArray(data) ? data : [];
        } catch {
            lookupsCache[key] = [];
        }
        return lookupsCache[key];
    };

    const scopeFor = (container) => container?.querySelector?.('[name="propertyId"], #hr-filter-property')?.value || '';

    const populateLookupsInContainer = async (container) => {
        if (!container) return;
        const scopeId = scopeFor(container);
        const selects = container.querySelectorAll('select[data-lookup]');
        for (const select of selects) {
            const kind = select.dataset.lookup;
            const currentVal = select.value;
            const options = await loadLookup(kind, scopedKinds.has(kind) ? scopeId : '');
            const firstPlaceholder = select.querySelector('option[value=""]')?.textContent || 'Selecione…';

            select.innerHTML = `<option value="">${esc(firstPlaceholder)}</option>` +
                options.map(opt => `<option value="${esc(opt.id)}">${esc(opt.label)}</option>`).join('');

            if (currentVal) select.value = currentVal;
        }
    };

    const selectedText = (form, name) => {
        const field = form.querySelector(`[name="${name}"]`);
        if (!field) return '';
        if (field.tagName === 'SELECT') return field.selectedOptions[0]?.textContent?.trim() || '';
        return field.value || '';
    };

    const nullGuids = (data, names) => {
        for (const name of names) if (!data[name]) data[name] = null;
    };

    const renderEmpty = (message) => {
        if (!content) return;
        content.innerHTML = `<div class="empty-state"><p class="empty">${esc(message)}</p></div>`;
    };

    const statusBadge = (status) => {
        const s = (status || '').toUpperCase();
        let cls = 'badge-neutral';
        let label = s;

        switch (s) {
            case 'ACTIVE':
            case 'OPEN':
            case 'AVAILABLE':
                cls = 'badge-success';
                label = s === 'AVAILABLE' ? 'Disponível' : (s === 'OPEN' ? 'Aberto' : 'Ativo');
                break;
            case 'IN_FIELD':
            case 'IN_PROGRESS':
            case 'INVESTIGATING':
            case 'IN_TRANSIT':
                cls = 'badge-warning';
                label = s === 'IN_FIELD' ? 'Em Campo' : (s === 'IN_TRANSIT' ? 'Em Trânsito' : (s === 'INVESTIGATING' ? 'Investigando' : 'Em Andamento'));
                break;
            case 'COMPLETED':
            case 'CLOSED':
                cls = 'badge-info';
                label = s === 'CLOSED' ? 'Encerrado' : 'Concluído';
                break;
            case 'INACTIVE':
            case 'CANCELLED':
            case 'DISCARDED':
                cls = 'badge-danger';
                label = s === 'DISCARDED' ? 'Descartado' : (s === 'CANCELLED' ? 'Cancelado' : 'Inativo');
                break;
            case 'DELIVERED':
                cls = 'badge-accent';
                label = 'Entregue';
                break;
            case 'RETURNED':
                cls = 'badge-neutral';
                label = 'Devolvido';
                break;
        }

        return `<span class="badge ${cls}">${esc(label)}</span>`;
    };

    const renderDashboard = (data) => {
        if (!content) return;
        const labels = {
            activePeople: 'Pessoas Ativas',
            activeTeams: 'Equipes Ativas',
            workedHours: 'Horas Trabalhadas',
            laborCost: 'Custo de Mão de Obra (R$)',
            expiredTrainings: 'Treinamentos Vencidos',
            expiredPpe: 'EPIs Vencidos',
            openIncidents: 'Incidentes em Aberto',
            overdueActions: 'Ações Corretivas em Atraso',
            teamsInField: 'Equipes em Campo',
            criticalAlerts: 'Alertas Críticos'
        };

        const grid = document.createElement('div');
        grid.className = 'metric-grid';

        if (data && typeof data === 'object') {
            for (const [key, value] of Object.entries(data)) {
                const article = document.createElement('article');
                article.className = 'metric-card';

                const strong = document.createElement('strong');
                strong.textContent = typeof value === 'number' && key === 'laborCost'
                    ? value.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' })
                    : String(value ?? 0);

                const span = document.createElement('span');
                span.textContent = labels[key] || key;

                article.appendChild(strong);
                article.appendChild(span);
                grid.appendChild(article);
            }
        }

        content.replaceChildren(grid);
    };

    const getActionsForItem = (item, currentRoute) => {
        const id = item.id;
        const status = (item.status || '').toUpperCase();
        const actions = [];

        if (currentRoute === 'people') {
            if (status === 'ACTIVE') {
                actions.push(`<button type="button" class="btn btn-sm btn-outline-danger" data-action="deactivate" data-id="${id}">Inativar</button>`);
            } else if (status === 'INACTIVE') {
                actions.push(`<button type="button" class="btn btn-sm btn-outline-success" data-action="activate" data-id="${id}">Ativar</button>`);
            }
        } else if (currentRoute === 'time-entries' || currentRoute === 'operations-board') {
            if (status === 'OPEN') {
                actions.push(`<button type="button" class="btn btn-sm btn-primary" data-action="end-journey" data-id="${id}">Encerrar Jornada</button>`);
            }
            if (status === 'CLOSED' && item.reviewStatus !== 'CONFIRMED') {
                actions.push(`<button type="button" class="btn btn-sm btn-success" data-action="confirm-journey" data-id="${id}">Conferir</button>`);
            }
            if (status !== 'CANCELLED') {
                actions.push(`<button type="button" class="btn btn-sm btn-outline-secondary" data-action="correct-journey" data-id="${id}">Corrigir</button>`);
            }
            if (item.reviewStatus === 'CONFIRMED') {
                actions.push(`<button type="button" class="btn btn-sm btn-primary" data-action="appropriate" data-id="${id}">Apropriar na safra</button>`);
            }
        } else if (currentRoute === 'allocations') {
            if (status === 'ACTIVE') {
                actions.push(`<button type="button" class="btn btn-sm btn-outline-danger" data-action="cancel-plan" data-id="${id}">Cancelar alocação</button>`);
            }
        } else if (currentRoute === 'teams') {
            if (status === 'ACTIVE') {
                actions.push(`<button type="button" class="btn btn-sm btn-primary" data-action="dispatch" data-id="${id}">Despachar p/ Campo</button>`);
                actions.push(`<button type="button" class="btn btn-sm btn-outline-danger" data-action="deactivate" data-id="${id}">Inativar</button>`);
            } else if (status === 'IN_FIELD') {
                actions.push(`<button type="button" class="btn btn-sm btn-success" data-action="activate" data-id="${id}">Retornar do Campo</button>`);
                actions.push(`<button type="button" class="btn btn-sm btn-outline-danger" data-action="deactivate" data-id="${id}">Inativar</button>`);
            } else if (status === 'INACTIVE') {
                actions.push(`<button type="button" class="btn btn-sm btn-outline-success" data-action="activate" data-id="${id}">Ativar</button>`);
            }
        } else if (currentRoute === 'trainings') {
            if (status === 'PLANNED') {
                actions.push(`<button type="button" class="btn btn-sm btn-primary" data-action="start" data-id="${id}">Iniciar</button>`);
                actions.push(`<button type="button" class="btn btn-sm btn-success" data-action="complete" data-id="${id}">Concluir</button>`);
                actions.push(`<button type="button" class="btn btn-sm btn-outline-danger" data-action="cancel" data-id="${id}">Cancelar</button>`);
            } else if (status === 'ACTIVE') {
                actions.push(`<button type="button" class="btn btn-sm btn-success" data-action="complete" data-id="${id}">Concluir</button>`);
                actions.push(`<button type="button" class="btn btn-sm btn-outline-danger" data-action="cancel" data-id="${id}">Cancelar</button>`);
            }
        } else if (currentRoute === 'ppe') {
            if (status === 'AVAILABLE' || status === 'ACTIVE') {
                actions.push(`<button type="button" class="btn btn-sm btn-primary" data-action="deliver" data-id="${id}">Entregar EPI</button>`);
                actions.push(`<button type="button" class="btn btn-sm btn-outline-danger" data-action="deactivate" data-id="${id}">Inativar</button>`);
            } else if (status === 'DELIVERED') {
                actions.push(`<button type="button" class="btn btn-sm btn-success" data-action="return" data-id="${id}">Devolver EPI</button>`);
                actions.push(`<button type="button" class="btn btn-sm btn-outline-danger" data-action="discard" data-id="${id}">Descartar</button>`);
            } else if (status === 'RETURNED') {
                actions.push(`<button type="button" class="btn btn-sm btn-primary" data-action="activate" data-id="${id}">Disponibilizar</button>`);
                actions.push(`<button type="button" class="btn btn-sm btn-outline-danger" data-action="discard" data-id="${id}">Descartar</button>`);
            }
        } else if (currentRoute === 'incidents') {
            if (status === 'OPEN') {
                actions.push(`<button type="button" class="btn btn-sm btn-primary" data-action="investigate" data-id="${id}">Investigar</button>`);
                actions.push(`<button type="button" class="btn btn-sm btn-outline-secondary" data-action="close" data-id="${id}">Encerrar</button>`);
            } else if (status === 'INVESTIGATING') {
                actions.push(`<button type="button" class="btn btn-sm btn-success" data-action="complete" data-id="${id}">Concluir</button>`);
                actions.push(`<button type="button" class="btn btn-sm btn-outline-secondary" data-action="close" data-id="${id}">Encerrar</button>`);
            }
        } else if (currentRoute === 'corrective-actions') {
            if (status === 'OPEN') {
                actions.push(`<button type="button" class="btn btn-sm btn-primary" data-action="start" data-id="${id}">Iniciar Ação</button>`);
                actions.push(`<button type="button" class="btn btn-sm btn-success" data-action="complete" data-id="${id}">Concluir</button>`);
                actions.push(`<button type="button" class="btn btn-sm btn-outline-danger" data-action="cancel" data-id="${id}">Cancelar</button>`);
            } else if (status === 'IN_PROGRESS') {
                actions.push(`<button type="button" class="btn btn-sm btn-success" data-action="complete" data-id="${id}">Concluir</button>`);
                actions.push(`<button type="button" class="btn btn-sm btn-outline-danger" data-action="cancel" data-id="${id}">Cancelar</button>`);
            }
        } else if (currentRoute === 'transport') {
            if (status === 'SCHEDULED') {
                actions.push(`<button type="button" class="btn btn-sm btn-primary" data-action="start" data-id="${id}">Iniciar Viagem</button>`);
                actions.push(`<button type="button" class="btn btn-sm btn-success" data-action="complete" data-id="${id}">Concluir</button>`);
                actions.push(`<button type="button" class="btn btn-sm btn-outline-danger" data-action="cancel" data-id="${id}">Cancelar</button>`);
            } else if (status === 'IN_TRANSIT') {
                actions.push(`<button type="button" class="btn btn-sm btn-success" data-action="complete" data-id="${id}">Concluir Viagem</button>`);
            }
        } else {
            // General resources (allocations, labor-costs, safety-risks, accommodations)
            if (status === 'ACTIVE' || status === 'OPEN') {
                actions.push(`<button type="button" class="btn btn-sm btn-outline-danger" data-action="deactivate" data-id="${id}">Inativar</button>`);
            } else if (status === 'INACTIVE') {
                actions.push(`<button type="button" class="btn btn-sm btn-outline-success" data-action="activate" data-id="${id}">Ativar</button>`);
            }
        }

        return actions.join(' ');
    };

    const formatAmount = (item) => {
        if (item.costState === 'UNAVAILABLE') return `<span class="cost-missing">Custo indisponível${item.blockReason ? `: ${esc(item.blockReason)}` : ''}</span>`;
        if (item.costState === 'CALCULATED') return Number(item.amount || 0).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
        if ((item.kind || '').toUpperCase() === 'TIME_ENTRY') return 'Não calculado';
        return item.amount ? Number(item.amount).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' }) : '—';
    };

    const boardQuery = () => {
        const params = new URLSearchParams();
        const from = document.querySelector('#hr-filter-from')?.value;
        const to = document.querySelector('#hr-filter-to')?.value;
        const propertyId = document.querySelector('#hr-filter-property')?.value;
        const seasonId = document.querySelector('#hr-filter-season')?.value;
        const teamId = document.querySelector('#hr-filter-team')?.value;
        if (from) params.set('from', from);
        if (to) params.set('to', to);
        if (propertyId) params.set('propertyId', propertyId);
        if (seasonId) params.set('seasonId', seasonId);
        if (teamId) params.set('teamId', teamId);
        if (boardSituation) params.set('situation', boardSituation);
        params.set('page', String(boardPage));
        params.set('pageSize', '25');
        const text = params.toString();
        return text ? `?${text}` : '';
    };

    const renderBoard = (data) => {
        if (!content) return;
        const wrap = document.createElement('div');
        const grid = document.createElement('div');
        grid.className = 'metric-grid';
        for (const metric of data.metrics || []) {
            const article = document.createElement('button');
            article.type = 'button';
            article.className = 'metric-card';
            article.dataset.situation = metric.code;
            article.setAttribute('aria-pressed', boardSituation === metric.code ? 'true' : 'false');
            const strong = document.createElement('strong');
            if (metric.amountState === 'VALUE' && metric.amount != null) strong.textContent = Number(metric.amount).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
            else if (metric.count != null) strong.textContent = String(metric.count);
            else if (metric.amountState === 'UNAVAILABLE') strong.textContent = 'Indisponível';
            else strong.textContent = 'Sem dados';
            const span = document.createElement('span');
            span.textContent = metric.label;
            article.append(strong, span);
            grid.appendChild(article);
        }
        wrap.appendChild(grid);
        const holder = document.createElement('div');
        wrap.appendChild(holder);
        content.replaceChildren(wrap);
        const previous = content;
        const fake = holder;
        const oldContent = content;
        content = fake;
        renderList(data.items || []);
        content = oldContent;
        if (!(data.items || []).length) holder.innerHTML = '<div class="empty-state"><p class="empty">Nenhum lançamento para este indicador e filtro.</p></div>';
        const total = Number(data.total || 0);
        const pageSize = Number(data.pageSize || 25);
        const page = Number(data.page || 1);
        if (total > pageSize) {
            const nav = document.createElement('div');
            nav.className = 'hr-pagination';
            const prev = document.createElement('button');
            prev.type = 'button';
            prev.className = 'btn btn-secondary';
            prev.dataset.boardPage = 'prev';
            prev.disabled = page <= 1;
            prev.textContent = 'Anterior';
            const next = document.createElement('button');
            next.type = 'button';
            next.className = 'btn btn-secondary';
            next.dataset.boardPage = 'next';
            next.disabled = page * pageSize >= total;
            next.textContent = 'Próxima';
            const label = document.createElement('span');
            label.textContent = `Página ${page} de ${Math.ceil(total / pageSize)} · ${total} registros`;
            nav.append(prev, label, next);
            wrap.appendChild(nav);
        }
    };

    const renderList = (rows) => {
        if (!content) return;
        if (!rows || rows.length === 0) {
            renderEmpty('Nenhum registro encontrado para os filtros selecionados.');
            return;
        }

        const table = document.createElement('div');
        table.className = 'table-responsive';
        table.innerHTML = `
            <table class="table hr-table">
                <thead>
                    <tr>
                        <th>Identificação / Nome</th>
                        <th>Status</th>
                        <th>Início / Lançamento</th>
                        <th>Término / Previsão</th>
                        <th>Valor / Custo</th>
                        <th>Ações</th>
                    </tr>
                </thead>
                <tbody>
                    ${rows.map(item => `
                        <tr>
                            <td>
                                <strong>${esc(item.name || '(Sem nome)')}</strong>
                                ${item.kind ? `<small class="d-block text-muted">${esc(item.kind)}</small>` : ''}
                            </td>
                            <td>${statusBadge(item.status)}</td>
                            <td>${item.startsAt ? new Date(item.startsAt).toLocaleString('pt-BR') : '—'}</td>
                            <td>${item.endsAt ? new Date(item.endsAt).toLocaleString('pt-BR') : '—'}</td>
                            <td>${formatAmount(item)}</td>
                            <td class="table-actions">
                                ${getActionsForItem(item, route)}
                            </td>
                        </tr>
                    `).join('')}
                </tbody>
            </table>
        `;

        content.replaceChildren(table);
    };

    const show = async () => {
        renderEmpty('Carregando dados operacionais…');
        const filterStatus = document.querySelector('#hr-filter-status')?.value;
        const filterSearch = document.querySelector('#hr-filter-search')?.value?.trim().toLowerCase();

        try {
            if (route === 'dashboard') {
                const data = await request('dashboard');
                renderDashboard(data);
                return;
            }
            if (route === 'operations-board') {
                const data = await request(`operations-board${boardQuery()}`);
                renderBoard(data);
                return;
            }
            if (route === 'tariffs') {
                const data = await request('tariffs');
                renderList((data || []).map(t => ({
                    id: t.id,
                    name: `${t.roleName || 'Qualquer cargo'} · ${t.activityType || 'qualquer atividade'} · ${t.rateType}`,
                    status: t.active ? 'ACTIVE' : 'INACTIVE',
                    kind: 'TARIFF',
                    amount: t.rateValue,
                    costState: 'CALCULATED',
                    startsAt: t.validFrom,
                    endsAt: t.validTo
                })));
                return;
            }
            if (route === 'data-reviews') {
                const data = await request('data-reviews');
                renderList((data || []).map(r => ({
                    id: r.entityId,
                    name: `${r.reasonCode}: ${r.detail}`,
                    status: r.resolution,
                    kind: r.entityTable,
                    amount: 0,
                    updatedAt: r.detectedAt
                })));
                return;
            }

            const query = filterStatus ? `?status=${encodeURIComponent(filterStatus)}` : '';
            const data = await request(`${route}${query}`);
            let rows = Array.isArray(data) ? data : [];

            if (filterSearch) {
                rows = rows.filter(r => (r.name || '').toLowerCase().includes(filterSearch));
            }

            renderList(rows);
        } catch (e) {
            renderEmpty('Não foi possível carregar os dados.');
            notify(e.message, true);
        }
    };

    // Toggle Help Guide
    if (helpBtn && helpPanel) {
        helpBtn.addEventListener('click', () => {
            const isHidden = helpPanel.hidden;
            helpPanel.hidden = !isHidden;
            helpBtn.setAttribute('aria-expanded', String(!isHidden));
        });
    }

    // Export CSV
    if (exportBtn) {
        exportBtn.addEventListener('click', async () => {
            if (route === 'dashboard') {
                notify('Aba dashboard não possui exportação tabular.', true);
                return;
            }
            try {
                const token = localStorage.getItem('agro360.accessToken');
                const resp = await fetch(`${api}/${route}/export`, {
                    headers: { Authorization: `Bearer ${token}` }
                });
                if (!resp.ok) throw new Error('Falha ao exportar CSV.');
                const blob = await resp.blob();
                const a = document.createElement('a');
                a.href = URL.createObjectURL(blob);
                a.download = `${route}-rh-rural.csv`;
                a.click();
                notify('CSV exportado com sucesso.');
            } catch (err) {
                notify(err.message, true);
            }
        });
    }

    // Tab Navigation
    document.querySelectorAll('[data-tab]').forEach(btn => {
        btn.addEventListener('click', () => {
            document.querySelectorAll('[data-tab]').forEach(b => b.classList.remove('active'));
            btn.classList.add('active');

            activeTab = btn.dataset.tab;
            if (routes[activeTab]) {
                route = routes[activeTab];
                show();
            }
        });
    });

    // Filters
    const filters = document.querySelector('#hr-filters');
    if (filters) {
        filters.addEventListener('submit', e => {
            e.preventDefault();
            boardPage = 1;
            show();
        });
        filters.addEventListener('change', async (e) => {
            if (e.target?.id !== 'hr-filter-property') return;
            Object.keys(lookupsCache).forEach(key => { if (key.startsWith('seasons@')) delete lookupsCache[key]; });
            await populateLookupsInContainer(filters);
        });
    }

    // New Record Button Opens Specific Dialog
    const newRecordBtn = document.querySelector('#new-hr-record');
    if (newRecordBtn) {
        newRecordBtn.addEventListener('click', async () => {
            if (route === 'dashboard') {
                notify('Selecione uma área operacional (Pessoas, Jornada, etc.) para criar registros.');
                return;
            }

            if (route === 'operations-board' || route === 'data-reviews') {
                notify('Este painel consulta os registros. Abra Pessoas, Alocações, Jornada ou Tarifas para incluir um novo.');
                return;
            }
            if (route === 'allocations') {
                const dialog = document.querySelector('#hr-allocation-dialog');
                const form = document.querySelector('#hr-allocation-form');
                form?.reset();
                const key = form?.querySelector('[name="idempotencyKey"]');
                if (key) key.value = newKey();
                const summary = document.querySelector('#hr-allocation-summary');
                if (summary) summary.hidden = true;
                if (form) delete form.dataset.confirmed;
                await populateLookupsInContainer(form);
                dialog?.showModal();
                form?.querySelector('input, select, textarea')?.focus();
                return;
            }
            if (route === 'tariffs') {
                const dialog = document.querySelector('#hr-tariff-dialog');
                const form = document.querySelector('#hr-tariff-form');
                form?.reset();
                const key = form?.querySelector('[name="idempotencyKey"]');
                if (key) key.value = newKey();
                const summary = document.querySelector('#hr-tariff-summary');
                if (summary) summary.hidden = true;
                if (form) delete form.dataset.confirmed;
                await populateLookupsInContainer(form);
                dialog?.showModal();
                form?.querySelector('input, select, textarea')?.focus();
                return;
            }
            if (route === 'people') {
                if (personDialog && personForm) {
                    personForm.reset();
                    await populateLookupsInContainer(personForm);
                    personDialog.showModal();
                }
            } else if (route === 'time-entries') {
                if (timeDialog && timeForm) {
                    timeForm.reset();
                    const timeKey = timeForm.querySelector('[name="idempotencyKey"]');
                    if (timeKey) timeKey.value = newKey();
                    await populateLookupsInContainer(timeForm);
                    const now = new Date();
                    now.setMinutes(now.getMinutes() - now.getTimezoneOffset());
                    const startedInput = timeForm.querySelector('[name="startedAt"]');
                    if (startedInput) startedInput.value = now.toISOString().slice(0, 16);
                    timeDialog.showModal();
                }
            } else if (route === 'transport') {
                if (transportDialog && transportForm) {
                    transportForm.reset();
                    await populateLookupsInContainer(transportForm);
                    const now = new Date();
                    now.setMinutes(now.getMinutes() - now.getTimezoneOffset());
                    const startInp = transportForm.querySelector('[name="startsAt"]');
                    const endInp = transportForm.querySelector('[name="endsAt"]');
                    if (startInp) startInp.value = now.toISOString().slice(0, 16);
                    const future = new Date(now.getTime() + 2 * 3600 * 1000);
                    if (endInp) endInp.value = future.toISOString().slice(0, 16);
                    transportDialog.showModal();
                }
            } else {
                if (recordDialog && recordForm) {
                    recordForm.reset();
                    await populateLookupsInContainer(recordForm);
                    const title = document.querySelector('#hr-record-title');
                    if (title) title.textContent = `Novo Registro: ${activeTab}`;
                    recordDialog.showModal();
                }
            }
        });
    }

    // Modal Close Buttons
    document.querySelectorAll('[data-close]').forEach(btn => {
        btn.addEventListener('click', () => {
            btn.closest('dialog')?.close();
        });
    });

    // Form Submissions
    if (personForm) {
        personForm.addEventListener('submit', async (e) => {
            e.preventDefault();
            if (!personForm.reportValidity()) return;
            const data = Object.fromEntries(new FormData(personForm));
            try {
                await request('people', { method: 'POST', body: JSON.stringify(data) });
                personDialog?.close();
                notify('Trabalhador cadastrado com sucesso!');
                delete lookupsCache['people'];
                await show();
            } catch (err) {
                notify(err.message, true);
            }
        });
    }

    if (timeForm) {
        timeForm.addEventListener('submit', async (e) => {
            e.preventDefault();
            if (!timeForm.reportValidity()) return;
            const data = Object.fromEntries(new FormData(timeForm));
            data.breakMinutes = Number(data.breakMinutes || 0);
            nullGuids(data, ['teamId', 'resourceId', 'allocationId']);
            if (!data.idempotencyKey) data.idempotencyKey = newKey();
            try {
                await request('time-entries/register', { method: 'POST', body: JSON.stringify(data) });
                timeDialog?.close();
                notify('Jornada de trabalho aberta com sucesso!');
                await show();
            } catch (err) {
                notify(err.message, true);
            }
        });
    }

    if (endTimeForm) {
        endTimeForm.addEventListener('submit', async (e) => {
            e.preventDefault();
            if (!endTimeForm.reportValidity()) return;
            const id = document.querySelector('#hr-end-time-id')?.value;
            const endedAt = document.querySelector('#hr-end-time-val')?.value;
            if (!id || !endedAt) return;
            try {
                await request(`time-entries/${id}/end`, {
                    method: 'POST',
                    body: JSON.stringify({ endedAt: endedAt })
                });
                endTimeDialog?.close();
                notify('Jornada encerrada com sucesso!');
                await show();
            } catch (err) {
                notify(err.message, true);
            }
        });
    }

    if (transportForm) {
        transportForm.addEventListener('submit', async (e) => {
            e.preventDefault();
            if (!transportForm.reportValidity()) return;
            const data = Object.fromEntries(new FormData(transportForm));
            data.capacity = Number(data.capacity || 20);
            data.passengerCount = Number(data.passengerCount || 0);
            try {
                await request('transport/schedule', { method: 'POST', body: JSON.stringify(data) });
                transportDialog?.close();
                notify('Transporte programado com sucesso!');
                await show();
            } catch (err) {
                notify(err.message, true);
            }
        });
    }

    if (recordForm) {
        recordForm.addEventListener('submit', async (e) => {
            e.preventDefault();
            if (!recordForm.reportValidity()) return;
            const data = Object.fromEntries(new FormData(recordForm));
            data.amount = Number(data.amount || 0);
            if (!data.personId) data.personId = null;
            if (!data.teamId) data.teamId = null;
            if (!data.propertyId) data.propertyId = null;
            if (!data.resourceId) data.resourceId = null;
            if (!data.startsAt) data.startsAt = null;
            if (!data.endsAt) data.endsAt = null;

            try {
                await request(route, { method: 'POST', body: JSON.stringify(data) });
                recordDialog?.close();
                notify('Registro salvo com sucesso!');
                if (route === 'teams') delete lookupsCache['teams'];
                await show();
            } catch (err) {
                notify(err.message, true);
            }
        });
    }

    // Contextual Action Buttons Click Handler
    if (content) {
        content.addEventListener('click', async (e) => {
            const metric = e.target.closest('button[data-situation]');
            if (metric && !metric.dataset.action) {
                const code = metric.dataset.situation;
                boardSituation = boardSituation === code ? '' : code;
                boardPage = 1;
                show();
                return;
            }
            const pager = e.target.closest('button[data-board-page]');
            if (pager && !pager.disabled) {
                boardPage += pager.dataset.boardPage === 'next' ? 1 : -1;
                if (boardPage < 1) boardPage = 1;
                show();
                return;
            }

            const btn = e.target.closest('button[data-action]');
            if (!btn) return;

            const action = btn.dataset.action;
            const id = btn.dataset.id;
            if (!id || !action) return;

            if (action === 'end-journey') {
                if (endTimeDialog && endTimeForm) {
                    const idInp = document.querySelector('#hr-end-time-id');
                    const valInp = document.querySelector('#hr-end-time-val');
                    if (idInp) idInp.value = id;
                    if (valInp) {
                        const now = new Date();
                        now.setMinutes(now.getMinutes() - now.getTimezoneOffset());
                        valInp.value = now.toISOString().slice(0, 16);
                    }
                    endTimeDialog.showModal();
                }
                return;
            }

            if (action === 'confirm-journey') {
                btn.disabled = true;
                try {
                    await request(`time-entries/${id}/confirm`, { method: 'POST' });
                    notify('Jornada conferida. O custo usa a tarifa vigente ou permanece indisponível.');
                    await show();
                } catch (err) {
                    notify(err.message, true);
                    btn.disabled = false;
                }
                return;
            }

            if (action === 'correct-journey') {
                const dialog = document.querySelector('#hr-correct-dialog');
                const form = document.querySelector('#hr-correct-form');
                form?.reset();
                const idInput = document.querySelector('#hr-correct-id');
                if (idInput) idInput.value = id;
                dialog?.showModal();
                form?.querySelector('textarea, input')?.focus();
                return;
            }

            if (action === 'appropriate') {
                btn.disabled = true;
                try {
                    const result = await request(`time-entries/${id}/appropriate`, {
                        method: 'POST',
                        body: JSON.stringify({ idempotencyKey: newKey() })
                    });
                    notify(result?.message || 'Apropriação registrada.');
                    await show();
                } catch (err) {
                    notify(err.message, true);
                    btn.disabled = false;
                }
                return;
            }

            if (action === 'cancel-plan') {
                const reason = window.prompt('Motivo do cancelamento da alocação. A jornada já executada permanece.');
                if (!reason || reason.trim().length < 5) {
                    notify('Informe um motivo com pelo menos 5 caracteres.', true);
                    return;
                }
                btn.disabled = true;
                try {
                    await request(`allocations/${id}/cancel-plan`, {
                        method: 'POST',
                        body: JSON.stringify({ status: 'CANCELLED', reason: reason.trim() })
                    });
                    notify('Alocação cancelada. A execução realizada foi preservada.');
                    await show();
                } catch (err) {
                    notify(err.message, true);
                    btn.disabled = false;
                }
                return;
            }

            const actionLabels = {
                'activate': 'ativar',
                'deactivate': 'inativar',
                'dispatch': 'despachar para o campo',
                'start': 'iniciar',
                'investigate': 'iniciar investigação',
                'complete': 'concluir',
                'close': 'encerrar',
                'cancel': 'cancelar',
                'deliver': 'entregar EPI',
                'return': 'devolver EPI',
                'discard': 'descartar'
            };

            const label = actionLabels[action] || action;
            if (!confirm(`Confirma a operação "${label}" para este registro?`)) return;

            try {
                await request(`${route}/${id}/${action}`, { method: 'POST' });
                notify(`Operação "${label}" concluída com sucesso!`);
                await show();
            } catch (err) {
                notify(err.message, true);
            }
        });
    }

    const clearScopeCache = () => {
        Object.keys(lookupsCache).forEach(key => {
            if ([...scopedKinds].some(kind => key.startsWith(`${kind}@`))) delete lookupsCache[key];
        });
    };

    const resetReview = (form, summarySelector, label) => {
        delete form.dataset.confirmed;
        const summary = document.querySelector(summarySelector);
        if (summary) summary.hidden = true;
        const button = form.querySelector('[type="submit"]');
        if (button) button.textContent = label;
    };

    const allocationForm = document.querySelector('#hr-allocation-form');
    if (allocationForm) {
        allocationForm.addEventListener('change', async (ev) => {
            resetReview(allocationForm, '#hr-allocation-summary', 'Revisar e salvar');
            if (ev.target?.name !== 'propertyId') return;
            clearScopeCache();
            await populateLookupsInContainer(allocationForm);
        });
        allocationForm.addEventListener('input', () => resetReview(allocationForm, '#hr-allocation-summary', 'Revisar e salvar'));
        allocationForm.addEventListener('submit', async (e) => {
            e.preventDefault();
            if (!allocationForm.reportValidity()) return;
            const summary = document.querySelector('#hr-allocation-summary');
            if (allocationForm.dataset.confirmed !== 'yes') {
                if (summary) {
                    summary.hidden = false;
                    summary.textContent = `Confirme a alocação “${selectedText(allocationForm, 'name')}”: ${selectedText(allocationForm, 'personId') || 'sem pessoa'}, ${selectedText(allocationForm, 'teamId') || 'sem equipe'}, ${selectedText(allocationForm, 'propertyId')}, talhão ${selectedText(allocationForm, 'plotId')}, safra ${selectedText(allocationForm, 'seasonId')}, ordem ${selectedText(allocationForm, 'orderId')}, atividade ${selectedText(allocationForm, 'activityType')}, de ${selectedText(allocationForm, 'startsAt')} até ${selectedText(allocationForm, 'endsAt')}.`;
                }
                allocationForm.dataset.confirmed = 'yes';
                const button = allocationForm.querySelector('[type="submit"]');
                if (button) button.textContent = 'Confirmar alocação';
                return;
            }
            const data = Object.fromEntries(new FormData(allocationForm));
            nullGuids(data, ['personId', 'teamId', 'plotId', 'seasonId', 'orderId']);
            const button = allocationForm.querySelector('[type="submit"]');
            if (button) button.disabled = true;
            try {
                await request('allocations/plan', { method: 'POST', body: JSON.stringify(data) });
                allocationForm.closest('dialog')?.close();
                notify('Alocação planejada.');
                delete lookupsCache.allocations;
                await show();
            } catch (err) {
                notify(err.message, true);
                resetReview(allocationForm, '#hr-allocation-summary', 'Revisar e salvar');
            } finally {
                if (button) button.disabled = false;
            }
        });
    }

    const tariffForm = document.querySelector('#hr-tariff-form');
    if (tariffForm) {
        tariffForm.addEventListener('input', () => resetReview(tariffForm, '#hr-tariff-summary', 'Revisar e salvar'));
        tariffForm.addEventListener('submit', async (e) => {
            e.preventDefault();
            if (!tariffForm.reportValidity()) return;
            const summary = document.querySelector('#hr-tariff-summary');
            if (tariffForm.dataset.confirmed !== 'yes') {
                if (summary) {
                    summary.hidden = false;
                    summary.textContent = `Confirme a tarifa ${selectedText(tariffForm, 'rateType')} de R$ ${selectedText(tariffForm, 'rateValue')} para ${selectedText(tariffForm, 'roleId') || 'qualquer cargo'} e ${selectedText(tariffForm, 'activityType') || 'qualquer atividade'}, vigente de ${selectedText(tariffForm, 'validFrom')} até ${selectedText(tariffForm, 'validTo') || 'sem término'}.`;
                }
                tariffForm.dataset.confirmed = 'yes';
                const button = tariffForm.querySelector('[type="submit"]');
                if (button) button.textContent = 'Confirmar tarifa';
                return;
            }
            const data = Object.fromEntries(new FormData(tariffForm));
            nullGuids(data, ['roleId']);
            if (!data.activityType) data.activityType = null;
            if (!data.validTo) data.validTo = null;
            data.rateValue = Number(data.rateValue);
            const button = tariffForm.querySelector('[type="submit"]');
            if (button) button.disabled = true;
            try {
                await request('tariffs', { method: 'POST', body: JSON.stringify(data) });
                tariffForm.closest('dialog')?.close();
                notify('Vigência de tarifa registrada.');
                await show();
            } catch (err) {
                notify(err.message, true);
                resetReview(tariffForm, '#hr-tariff-summary', 'Revisar e salvar');
            } finally {
                if (button) button.disabled = false;
            }
        });
    }

    const correctForm = document.querySelector('#hr-correct-form');
    if (correctForm) {
        correctForm.addEventListener('submit', async (e) => {
            e.preventDefault();
            if (!correctForm.reportValidity()) return;
            const data = Object.fromEntries(new FormData(correctForm));
            const id = data.entryId;
            if (!data.endedAt) data.endedAt = null;
            data.breakMinutes = Number(data.breakMinutes || 0);
            data.pieceQuantity = data.pieceQuantity === '' ? null : Number(data.pieceQuantity);
            delete data.entryId;
            const button = correctForm.querySelector('[type="submit"]');
            if (button) button.disabled = true;
            try {
                await request(`time-entries/${id}/correct`, { method: 'POST', body: JSON.stringify(data) });
                correctForm.closest('dialog')?.close();
                notify('Correção registrada. A jornada voltou para conferência.');
                await show();
            } catch (err) {
                notify(err.message, true);
            } finally {
                if (button) button.disabled = false;
            }
        });
    }

    populateLookupsInContainer(document.querySelector('#hr-filters')).finally(() => show());
})();
