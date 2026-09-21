(()=>{'use strict';
const api = localStorage.getItem('agro360.api') || 'https://localhost:7081';
const token = () => sessionStorage.getItem('portal.token');

function sanitizeMessage(message = '') {
    return String(message).replace(/[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}/g, '[identificador]');
}

function showToast(message, error = false) {
    const region = document.querySelector('#portal-toast-region');
    if (!region) return;
    const toast = document.createElement('div');
    toast.className = `portal-toast ${error ? 'error' : 'success'}`;
    toast.textContent = sanitizeMessage(message);
    region.appendChild(toast);
    setTimeout(() => toast.classList.add('visible'), 10);
    setTimeout(() => {
        toast.classList.remove('visible');
        setTimeout(() => toast.remove(), 300);
    }, 4500);
}

async function request(path, options = {}) {
    const headers = { 'Content-Type': 'application/json', ...options.headers };
    if (token()) headers.Authorization = `Bearer ${token()}`;
    let response;
    try {
        response = await fetch(api + path, { ...options, headers });
    } catch (e) {
        throw new Error('Falha de conexão com os serviços da plataforma. Verifique sua rede.');
    }
    if (response.status === 401 && !path.includes('/access/')) {
        sessionStorage.clear();
        location.href = '/Portal/Login';
        throw new Error('Sua sessão expirou. Entre novamente.');
    }
    if (response.status === 403) {
        throw new Error('Acesso não autorizado para o seu perfil ou organização.');
    }
    if (response.status === 204) return null;
    const isJson = (response.headers.get('content-type') || '').includes('application/json');
    const body = isJson ? await response.json().catch(() => null) : null;
    if (!response.ok) {
        const msg = body?.detail || body?.title || `Operação não concluída (código ${response.status}).`;
        throw new Error(sanitizeMessage(msg));
    }
    return body;
}

const feedback = (form, message, error = false) => {
    const node = form.querySelector('.form-feedback');
    if (!node) return;
    node.textContent = sanitizeMessage(message);
    node.classList.toggle('error', error);
};

const submit = async (form, action) => {
    if (!form.reportValidity()) return;
    const button = form.querySelector('[type=submit]');
    if (button) button.disabled = true;
    feedback(form, 'Enviando…');
    try {
        await action(new FormData(form));
    } catch (e) {
        feedback(form, e.message, true);
        showToast(e.message, true);
    } finally {
        if (button) button.disabled = false;
    }
};

function confirmAction(title, message, callback) {
    const dialog = document.querySelector('#portal-confirm-dialog');
    if (!dialog) {
        callback('Confirmado pelo operador.');
        return;
    }
    const form = document.querySelector('#portal-confirm-form');
    document.querySelector('#portal-confirm-title').textContent = title;
    document.querySelector('#portal-confirm-message').textContent = message;
    const reasonInput = document.querySelector('#portal-confirm-reason');
    reasonInput.value = '';
    feedback(form, '');

    const cleanup = () => {
        dialog.close();
        form.onsubmit = null;
        document.querySelector('#portal-confirm-cancel').onclick = null;
        dialog.querySelector('.dialog-close').onclick = null;
    };

    document.querySelector('#portal-confirm-cancel').onclick = cleanup;
    dialog.querySelector('.dialog-close').onclick = cleanup;

    form.onsubmit = e => {
        e.preventDefault();
        const reason = reasonInput.value.trim();
        if (reason.length < 5) {
            feedback(form, 'Informe uma justificativa com no mínimo 5 caracteres.', true);
            return;
        }
        cleanup();
        callback(reason);
    };

    dialog.showModal();
}

// Logout
document.querySelector('#portal-logout')?.addEventListener('click', () => {
    sessionStorage.clear();
    location.href = '/Portal/Login';
});

// Login
const login = document.querySelector('#portal-login');
login?.addEventListener('submit', e => {
    e.preventDefault();
    submit(login, async d => {
        const auth = await request('/api/portal/access/login', {
            method: 'POST',
            body: JSON.stringify(Object.fromEntries(d))
        });
        sessionStorage.setItem('portal.token', auth.accessToken);
        sessionStorage.setItem('portal.profile', auth.profile);
        sessionStorage.setItem('portal.name', auth.name);
        sessionStorage.setItem('portal.email', auth.email || d.get('email'));
        location.href = '/Portal';
    });
});

// Accept Invitation
const accept = document.querySelector('#portal-accept');
accept?.addEventListener('submit', e => {
    e.preventDefault();
    submit(accept, async d => {
        const auth = await request('/api/portal/access/accept-invitation', {
            method: 'POST',
            body: JSON.stringify({
                token: d.get('token'),
                password: d.get('password'),
                acceptTerms: d.get('acceptTerms') === 'on'
            })
        });
        sessionStorage.setItem('portal.token', auth.accessToken);
        sessionStorage.setItem('portal.profile', auth.profile);
        sessionStorage.setItem('portal.name', auth.name);
        feedback(accept, 'Acesso ativado com sucesso. Abrindo o portal…');
        showToast('Primeiro acesso concluído!');
        setTimeout(() => location.href = '/Portal', 700);
    });
});

// Dashboard
const dashboard = document.querySelector('#portal-dashboard');
if (dashboard) {
    request('/api/portal/dashboard').then(data => {
        dashboard.querySelector('[data-name]').textContent = data.name.split(' ')[0];
        const profileElem = dashboard.querySelector('[data-profile]');
        if (profileElem) {
            profileElem.textContent = `Acesso configurado para o perfil ${data.profile.replaceAll('_', ' ').toLowerCase()}.`;
        }
        dashboard.querySelector('[data-metrics]').innerHTML = data.metrics.map(x =>
            `<a class="metric-card" href="${x.target || '#'}">
                <strong>${x.value}</strong>
                <span>${x.label}</span>
                <i>Ver detalhes →</i>
            </a>`
        ).join('');

        const annRoot = dashboard.querySelector('[data-announcements]');
        if (annRoot) {
            annRoot.innerHTML = data.announcements.length
                ? data.announcements.map(x =>
                    `<article class="announcement ${x.read ? 'read' : ''}">
                        <span class="status ${x.severity.toLowerCase()}">${x.severity}</span>
                        <div>
                            <h3>${escapeHtml(x.title)}</h3>
                            <p>${escapeHtml(x.summary)}</p>
                        </div>
                        <button class="secondary button" data-read="${x.id}" ${x.read ? 'disabled' : ''}>
                            ${x.read ? 'Lido' : 'Marcar como lido'}
                        </button>
                    </article>`
                ).join('')
                : '<div class="empty"><strong>Tudo em dia por aqui.</strong><span>Novos comunicados aparecerão neste espaço.</span></div>';
        }

        const actRoot = dashboard.querySelector('[data-activities]');
        if (actRoot) {
            actRoot.innerHTML = data.activities.length
                ? data.activities.map(a =>
                    `<article class="activity-card">
                        <span class="status">${status(a.type)}</span>
                        <div>
                            <p>${escapeHtml(a.description)}</p>
                            <small>${new Date(a.occurredAt).toLocaleString('pt-BR')}</small>
                        </div>
                    </article>`
                ).join('')
                : '<div class="empty"><span>Nenhuma atividade recente registrada.</span></div>';
        }
    }).catch(e => {
        const metrics = dashboard.querySelector('[data-metrics]');
        if (metrics) metrics.innerHTML = `<div class="empty error">${escapeHtml(e.message)}</div>`;
    });

    dashboard.addEventListener('click', async e => {
        const button = e.target.closest('[data-read]');
        if (!button || button.disabled) return;
        try {
            await request(`/api/portal/announcements/${button.dataset.read}/read`, { method: 'POST' });
            button.textContent = 'Lido';
            button.disabled = true;
            button.closest('.announcement')?.classList.add('read');
            showToast('Comunicado marcado como lido.');
        } catch (err) {
            showToast(err.message, true);
        }
    });
}

// Marketplace & Cotações
const market = document.querySelector('#portal-marketplace');
async function loadListings() {
    const form = document.querySelector('#marketplace-filter');
    const params = new URLSearchParams();
    if (form) {
        new FormData(form).forEach((v, k) => { if (v) params.set(k, v); });
    }
    const root = market.querySelector('[data-listings]');
    try {
        const data = await request('/api/portal/marketplace?' + params);
        root.innerHTML = data.length
            ? data.map(x =>
                `<article class="listing-card">
                    <div class="listing-top">
                        <span>${escapeHtml(x.crop || 'Produto agro')}</span>
                        <span class="status available">Disponível</span>
                    </div>
                    <h2>${escapeHtml(x.product)}</h2>
                    <p>${escapeHtml([x.harvest, x.region].filter(Boolean).join(' · ') || 'Origem autorizada')}</p>
                    <div class="certs">
                        ${x.certifications.map(c => `<span>✓ ${escapeHtml(c)}</span>`).join('')}
                    </div>
                    <div class="listing-bottom">
                        <div>
                            <strong>${Number(x.availableQuantity).toLocaleString('pt-BR')} ${escapeHtml(x.unit)}</strong>
                            <small>${x.unitPrice ? 'A partir de ' + Number(x.unitPrice).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' }) + ' / ' + x.unit : 'Preço sob consulta'}</small>
                        </div>
                        <button class="primary button" data-quote="${x.id}" data-name="${escapeHtml(x.product)}" type="button">
                            Solicitar Cotação
                        </button>
                    </div>
                </article>`
            ).join('')
            : '<div class="empty"><strong>Nenhuma oferta para estes filtros.</strong><span>Altere os filtros ou volte mais tarde. Não exibimos disponibilidade fictícia.</span></div>';
    } catch (e) {
        root.innerHTML = `<div class="empty error">${escapeHtml(e.message)}</div>`;
    }
}

async function loadMyQuotes() {
    const root = document.querySelector('#my-quotes-list');
    if (!root) return;
    try {
        const quotes = await request('/api/portal/marketplace/my-quotes');
        root.innerHTML = quotes.length
            ? quotes.map(q =>
                `<article class="request-card">
                    <div>
                        <span class="status ${q.status.toLowerCase()}">${status(q.status)}</span>
                        <h2>${escapeHtml(q.protocol)}</h2>
                        <p>${q.items.map(i => `${Number(i.quantity).toLocaleString('pt-BR')} ${escapeHtml(i.unit)} de ${escapeHtml(i.product)}`).join(' · ')}</p>
                        <small>Enviada em ${new Date(q.createdAt).toLocaleDateString('pt-BR')} ${q.notes ? '· Obs: ' + escapeHtml(q.notes) : ''}</small>
                    </div>
                </article>`
            ).join('')
            : '<div class="empty"><strong>Você ainda não enviou cotações.</strong><span>Consulte o catálogo de ofertas e envie uma cotação para negociar condições.</span></div>';
    } catch (e) {
        root.innerHTML = `<div class="empty error">${escapeHtml(e.message)}</div>`;
    }
}

if (market) {
    loadListings();
    document.querySelector('#marketplace-filter')?.addEventListener('submit', e => {
        e.preventDefault();
        loadListings();
    });

    const quoteDialog = document.querySelector('#quote-dialog');
    const quoteForm = document.querySelector('#quote-form');

    market.addEventListener('click', e => {
        const b = e.target.closest('[data-quote]');
        if (!b) return;
        quoteForm.listingId.value = b.dataset.quote;
        quoteForm.querySelector('[data-quote-title]').textContent = b.dataset.name;
        quoteDialog.showModal();
    });

    quoteDialog?.querySelector('.dialog-close')?.addEventListener('click', () => quoteDialog.close());
    quoteDialog?.querySelector('[data-close-quote]')?.addEventListener('click', () => quoteDialog.close());

    quoteForm?.addEventListener('submit', e => {
        e.preventDefault();
        submit(quoteForm, async d => {
            await request('/api/portal/marketplace/quotes', {
                method: 'POST',
                body: JSON.stringify({
                    contactName: d.get('contactName'),
                    contactEmail: d.get('contactEmail'),
                    notes: d.get('notes'),
                    items: [{ listingId: d.get('listingId'), quantity: Number(d.get('quantity')) }]
                })
            });
            feedback(quoteForm, 'Cotação enviada com sucesso! Acompanhe o status em Minhas Cotações.');
            showToast('Cotação enviada!');
            quoteForm.reset();
            setTimeout(() => {
                quoteDialog.close();
                loadMyQuotes();
            }, 1000);
        });
    });

    const btnToggleQuotes = document.querySelector('#btn-toggle-my-quotes');
    const quotesRegion = document.querySelector('#portal-my-quotes-region');
    const catalogRegion = document.querySelector('#portal-catalog-region');
    const btnBackCatalog = document.querySelector('#btn-back-to-catalog');

    btnToggleQuotes?.addEventListener('click', () => {
        catalogRegion.style.display = 'none';
        quotesRegion.style.display = 'block';
        loadMyQuotes();
    });

    btnBackCatalog?.addEventListener('click', () => {
        quotesRegion.style.display = 'none';
        catalogRegion.style.display = 'block';
    });
}

// Solicitações
const requests = document.querySelector('#portal-requests');
async function loadRequests() {
    const root = requests.querySelector('[data-requests]');
    try {
        const data = await request('/api/portal/requests');
        root.innerHTML = data.length
            ? data.map(x =>
                `<article class="request-card">
                    <div>
                        <span class="status ${x.status.toLowerCase()}">${status(x.status)}</span>
                        <h2>${escapeHtml(x.subject)}</h2>
                        <p>${escapeHtml(x.protocol)} · Prioridade: ${priority(x.priority)} · Atualizada em ${new Date(x.updatedAt).toLocaleDateString('pt-BR')}</p>
                    </div>
                    <div style="display:flex; align-items:center; gap:8px;">
                        <button class="secondary button" data-detail="${x.id}" type="button">Detalhes</button>
                    </div>
                </article>`
            ).join('')
            : '<div class="empty"><strong>Você ainda não abriu solicitações.</strong><span>Quando precisar de suporte, laudos ou cotações, utilize o botão acima.</span></div>';
    } catch (e) {
        root.innerHTML = `<div class="empty error">${escapeHtml(e.message)}</div>`;
    }
}

if (requests) {
    loadRequests();
    const reqDialog = document.querySelector('#request-dialog');
    const reqForm = document.querySelector('#request-form');
    const detailDialog = document.querySelector('#request-detail-dialog');

    requests.querySelector('[data-open-request]')?.addEventListener('click', () => reqDialog.showModal());
    if (new URLSearchParams(location.search).has('new')) reqDialog.showModal();

    reqDialog?.querySelector('.dialog-close')?.addEventListener('click', () => reqDialog.close());
    reqDialog?.querySelector('[data-close-dialog]')?.addEventListener('click', () => reqDialog.close());

    reqForm?.addEventListener('submit', e => {
        e.preventDefault();
        submit(reqForm, async d => {
            await request('/api/portal/requests', {
                method: 'POST',
                body: JSON.stringify(Object.fromEntries(d))
            });
            feedback(reqForm, 'Solicitação registrada com sucesso! Acompanhe o atendimento pelo protocolo.');
            showToast('Solicitação registrada.');
            await loadRequests();
            setTimeout(() => {
                reqDialog.close();
                reqForm.reset();
            }, 1000);
        });
    });

    // Detalhe de Solicitação
    requests.addEventListener('click', async e => {
        const btn = e.target.closest('[data-detail]');
        if (!btn) return;
        const id = btn.dataset.detail;
        try {
            const detail = await request(`/api/portal/requests/${id}`);
            document.querySelector('#detail-protocol').textContent = detail.protocol;
            document.querySelector('#detail-subject').textContent = detail.subject;
            document.querySelector('#detail-type').textContent = detail.type.replaceAll('_', ' ');
            document.querySelector('#detail-priority').textContent = priority(detail.priority);
            document.querySelector('#detail-created').textContent = new Date(detail.createdAt).toLocaleString('pt-BR');
            document.querySelector('#detail-description').textContent = detail.description;

            const stElem = document.querySelector('#detail-status');
            stElem.textContent = status(detail.status);
            stElem.className = `status ${detail.status.toLowerCase()}`;

            const resBox = document.querySelector('#detail-resolution-box');
            if (detail.resolution) {
                document.querySelector('#detail-resolution').textContent = detail.resolution;
                resBox.style.display = 'block';
            } else {
                resBox.style.display = 'none';
            }

            const canBox = document.querySelector('#detail-cancellation-box');
            if (detail.cancellationReason) {
                document.querySelector('#detail-cancellation').textContent = detail.cancellationReason;
                canBox.style.display = 'block';
            } else {
                canBox.style.display = 'none';
            }

            const timeline = document.querySelector('#detail-timeline');
            timeline.innerHTML = detail.events.map(ev =>
                `<div class="timeline-item">
                    <span class="status">${status(ev.eventType)}</span>
                    <p>${escapeHtml(ev.message)}</p>
                    <small>${new Date(ev.createdAt).toLocaleString('pt-BR')}</small>
                </div>`
            ).join('');

            const btnCancel = document.querySelector('#btn-cancel-request');
            if (['OPEN', 'IN_REVIEW', 'WAITING_RESPONSE'].includes(detail.status)) {
                btnCancel.style.display = 'inline-flex';
                btnCancel.onclick = () => {
                    confirmAction('Cancelar Solicitação', 'Informe o motivo do cancelamento desta solicitação:', async reason => {
                        try {
                            await request(`/api/portal/requests/${id}/cancel`, {
                                method: 'POST',
                                body: JSON.stringify({ reason })
                            });
                            showToast('Solicitação cancelada.');
                            detailDialog.close();
                            await loadRequests();
                        } catch (err) {
                            showToast(err.message, true);
                        }
                    });
                };
            } else {
                btnCancel.style.display = 'none';
            }

            detailDialog.showModal();
        } catch (err) {
            showToast(err.message, true);
        }
    });

    detailDialog?.querySelector('.dialog-close')?.addEventListener('click', () => detailDialog.close());
    detailDialog?.querySelector('[data-close-detail]')?.addEventListener('click', () => detailDialog.close());
}

// Rastreabilidade
const traceSection = document.querySelector('#portal-traceability');
if (traceSection) {
    const traceForm = document.querySelector('#traceability-search-form');
    const traceRegion = document.querySelector('#traceability-result-region');

    traceForm?.addEventListener('submit', async e => {
        e.preventDefault();
        const code = document.querySelector('#traceability-input').value.trim();
        if (!code) return;
        traceRegion.innerHTML = '<div class="empty">Buscando rastreabilidade do lote…</div>';

        try {
            const trace = await request(`/api/portal/traceability/${encodeURIComponent(code)}`);
            if (!trace) {
                traceRegion.innerHTML = '<div class="empty alert"><strong>Lote não localizado.</strong><span>Verifique o código digitado ou entre em contato com o emissor.</span></div>';
                return;
            }

            const isApproved = trace.finalStatus === 'LIBERADO' || trace.finalStatus === 'APPROVED';
            traceRegion.innerHTML = `
                <article class="trace-card">
                    <div class="trace-header">
                        <div>
                            <span class="eyebrow">Dossiê Público de Rastreabilidade</span>
                            <h2>${escapeHtml(trace.product)}</h2>
                            <p>Lote: <strong>${escapeHtml(trace.lot)}</strong> · Código Público: <code>${escapeHtml(trace.publicCode)}</code></p>
                        </div>
                        <span class="status ${isApproved ? 'success' : 'warning'}">
                            ${isApproved ? '✓ Rastreabilidade Liberada' : '⚠️ Lote com Restrição / Em Análise'}
                        </span>
                    </div>

                    <div class="trace-info-grid">
                        <div>
                            <span class="detail-label">Origem Declarada</span>
                            <strong>${escapeHtml(trace.generalOrigin || 'Origem Rastreável')}</strong>
                        </div>
                        <div>
                            <span class="detail-label">Fazenda / Unidade</span>
                            <span>${escapeHtml(trace.farm || 'Confidencial / Protegido')}</span>
                        </div>
                        <div>
                            <span class="detail-label">Safra / Cultura</span>
                            <span>${escapeHtml([trace.season, trace.crop].filter(Boolean).join(' · ') || 'Agrícola')}</span>
                        </div>
                        <div>
                            <span class="detail-label">Atualização</span>
                            <small>${new Date(trace.updatedAt).toLocaleDateString('pt-BR')}</small>
                        </div>
                    </div>

                    <div class="trace-section">
                        <span class="detail-label">Certificações e Conformidades Publicáveis</span>
                        <div class="certs">
                            ${trace.publicCertifications && trace.publicCertifications.length
                                ? trace.publicCertifications.map(c => `<span class="cert-badge">✓ ${escapeHtml(c)}</span>`).join('')
                                : '<span>Nenhuma certificação especial vinculada.</span>'}
                        </div>
                    </div>

                    <div class="trace-section">
                        <span class="detail-label">Timeline de Etapas Autorizadas</span>
                        <div class="portal-timeline">
                            ${trace.events && trace.events.length
                                ? trace.events.map(ev =>
                                    `<div class="timeline-item">
                                        <span class="status">${escapeHtml(ev.status)}</span>
                                        <strong>${escapeHtml(ev.stage)}</strong>
                                        <small>${ev.occurredAt ? new Date(ev.occurredAt).toLocaleString('pt-BR') : 'Data confirmada'}</small>
                                    </div>`
                                ).join('')
                                : '<p>Aguardando registro de etapas adicionais.</p>'}
                        </div>
                    </div>
                </article>
            `;
        } catch (err) {
            traceRegion.innerHTML = `<div class="empty error"><strong>Falha na consulta:</strong><span>${escapeHtml(err.message)}</span></div>`;
        }
    });
}

// Documentos
const docSection = document.querySelector('#portal-documents');
if (docSection) {
    async function loadDocuments() {
        const root = document.querySelector('#portal-documents-list');
        try {
            const docs = await request('/api/portal/documents');
            root.innerHTML = docs.length
                ? docs.map(d =>
                    `<article class="request-card">
                        <div>
                            <span class="status success">${escapeHtml(d.documentType || 'Documento')}</span>
                            <h2>${escapeHtml(d.name)}</h2>
                            <p>Tamanho: ${(d.fileSize / 1024).toFixed(1)} KB · Liberado em ${new Date(d.createdAt).toLocaleDateString('pt-BR')}</p>
                        </div>
                        <button class="primary button" data-download="${d.documentId}" type="button">
                            Baixar Arquivo
                        </button>
                    </article>`
                ).join('')
                : '<div class="empty"><strong>Nenhum documento liberado no momento.</strong><span>Seus certificados, laudos e comprovantes aparecerão aqui assim que emitidos.</span></div>';
        } catch (e) {
            root.innerHTML = `<div class="empty error">${escapeHtml(e.message)}</div>`;
        }
    }
    loadDocuments();

    docSection.addEventListener('click', async e => {
        const btn = e.target.closest('[data-download]');
        if (!btn) return;
        const id = btn.dataset.download;
        try {
            btn.disabled = true;
            btn.textContent = 'Baixando…';
            const res = await fetch(`${api}/api/portal/documents/${id}/download`, {
                headers: { Authorization: `Bearer ${token()}` }
            });
            if (!res.ok) throw new Error('Download não autorizado ou arquivo indisponível.');
            const blob = await res.blob();
            const filename = res.headers.get('content-disposition')?.split('filename=')[1]?.replace(/"/g, '') || `documento-${id}.pdf`;
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = filename;
            document.body.appendChild(a);
            a.click();
            a.remove();
            URL.revokeObjectURL(url);
            showToast('Download concluído.');
        } catch (err) {
            showToast(err.message, true);
        } finally {
            btn.disabled = false;
            btn.textContent = 'Baixar Arquivo';
        }
    });
}

// Suporte e Central de Ajuda
const supportSection = document.querySelector('#portal-support');
if (supportSection) {
    async function loadArticles(search = '') {
        const root = document.querySelector('#support-articles-list');
        const query = search ? `?search=${encodeURIComponent(search)}` : '';
        try {
            const articles = await request(`/api/portal/support/articles${query}`);
            root.innerHTML = articles.length
                ? articles.map(a =>
                    `<article class="article-card" data-article-id="${a.id}">
                        <span class="eyebrow">${escapeHtml(a.category || 'GUIA')}</span>
                        <h3>${escapeHtml(a.title)}</h3>
                        <p>${escapeHtml(a.content.substring(0, 160))}…</p>
                        <button class="secondary button" data-read-article="${a.id}" type="button">Ler artigo completo →</button>
                    </article>`
                ).join('')
                : '<div class="empty"><strong>Nenhum artigo encontrado.</strong><span>Tente outros termos de busca ou abra uma solicitação para nossa equipe.</span></div>';
        } catch (e) {
            root.innerHTML = `<div class="empty error">${escapeHtml(e.message)}</div>`;
        }
    }
    loadArticles();

    document.querySelector('#support-search-form')?.addEventListener('submit', e => {
        e.preventDefault();
        loadArticles(document.querySelector('#support-search-input').value.trim());
    });

    const artDialog = document.querySelector('#article-dialog');
    supportSection.addEventListener('click', async e => {
        const btn = e.target.closest('[data-read-article]');
        if (!btn) return;
        const card = btn.closest('.article-card');
        document.querySelector('#article-title').textContent = card.querySelector('h3').textContent;
        document.querySelector('#article-category').textContent = card.querySelector('.eyebrow').textContent;
        document.querySelector('#article-content').innerHTML = `<p>${escapeHtml(card.querySelector('p').textContent)}</p>`;
        artDialog.showModal();
    });
    artDialog?.querySelector('.dialog-close')?.addEventListener('click', () => artDialog.close());
    artDialog?.querySelector('[data-close-article]')?.addEventListener('click', () => artDialog.close());
}

// Meu Perfil e Troca de Senha
const profileSection = document.querySelector('#portal-profile-page');
if (profileSection) {
    document.querySelector('#profile-name').textContent = sessionStorage.getItem('portal.name') || 'Parceiro';
    document.querySelector('#profile-email').textContent = sessionStorage.getItem('portal.email') || '-';
    const profileRole = sessionStorage.getItem('portal.profile') || 'Externo';
    document.querySelector('#profile-role').textContent = profileRole.replaceAll('_', ' ');

    const pwdForm = document.querySelector('#profile-password-form');
    pwdForm?.addEventListener('submit', e => {
        e.preventDefault();
        const currentPassword = pwdForm.currentPassword.value;
        const newPassword = pwdForm.newPassword.value;
        const confirmPassword = pwdForm.confirmPassword.value;

        if (newPassword !== confirmPassword) {
            feedback(pwdForm, 'A nova senha e a confirmação não conferem.', true);
            return;
        }

        submit(pwdForm, async () => {
            await request('/api/portal/access/change-password', {
                method: 'POST',
                body: JSON.stringify({ currentPassword, newPassword })
            });
            feedback(pwdForm, 'Senha alterada com sucesso!');
            showToast('Senha atualizada com segurança.');
            pwdForm.reset();
        });
    });
}

function status(value) {
    return ({
        OPEN: 'Aberta',
        IN_REVIEW: 'Em análise',
        WAITING_RESPONSE: 'Aguardando resposta',
        RESOLVED: 'Resolvida',
        REJECTED: 'Rejeitada',
        CANCELLED: 'Cancelada',
        REQUESTED: 'Solicitada',
        PROPOSED: 'Proposta enviada',
        ACCEPTED: 'Aceita',
        CONVERTED: 'Convertida',
        CREATED: 'Criada',
        APPROVED: 'Aprovada'
    })[value] || value;
}

function priority(value) {
    return ({
        LOW: 'Baixa',
        MEDIUM: 'Normal',
        HIGH: 'Alta',
        CRITICAL: 'Crítica'
    })[value] || value;
}

function escapeHtml(value = '') {
    const div = document.createElement('div');
    div.textContent = value;
    return div.innerHTML;
}
})();
