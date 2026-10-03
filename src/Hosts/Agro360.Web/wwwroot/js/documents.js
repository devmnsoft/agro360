(() => {
    'use strict';
    const base = document.querySelector('meta[name="api-base"]')?.content || '';
    const token = () => localStorage.getItem('agro360.token');
    const headers = json => ({
        ...(json ? { 'Content-Type': 'application/json' } : {}),
        ...(token() ? { Authorization: `Bearer ${token()}` } : {})
    });

    let view = 'dashboard';
    const content = document.querySelector('#document-content');
    const metrics = document.querySelector('#document-metrics');
    const pagination = document.querySelector('#document-pagination');
    const upload = document.querySelector('#upload-dialog');
    const action = document.querySelector('#action-dialog');
    const detailDialog = document.querySelector('#detail-dialog');
    const typeFilter = document.querySelector('#document-type-filter');

    const esc = v => {
        const d = document.createElement('div');
        d.textContent = v ?? '';
        return d.innerHTML;
    };

    async function api(path, o = {}) {
        const r = await fetch(base + path, {
            ...o,
            headers: { ...headers(!(o.body instanceof FormData)), ...o.headers }
        });
        if (!r.ok) {
            const e = await r.json().catch(() => ({ title: 'Não foi possível concluir a operação.' }));
            throw new Error(e.detail || e.title || Object.values(e.errors || {}).flat()[0] || 'Falha na operação.');
        }
        return r.status === 204 ? null : r.json();
    }

    async function loadDocumentTypes() {
        if (!typeFilter || typeFilter.options.length > 1) return;
        try {
            const types = await api('/api/documents/types');
            types.forEach(t => typeFilter.add(new Option(t.label, t.id)));
        } catch { /* silently ignore if endpoint unavailable */ }
    }

    function table(rows, kind) {
        if (!rows?.length) {
            return '<p class="empty-state">Nenhum registro encontrado. Os dados aparecerão aqui após a primeira operação.</p>';
        }
        const configs = {
            documents: [['Nome', 'name'], ['Tipo', 'typeName'], ['Status', 'status'], ['Versão', 'currentVersion'], ['Último envio', 'uploadedAt']],
            evidences: [['Documento', 'documentName'], ['Origem', 'origin'], ['Status', 'status'], ['Evento', 'eventAt']],
            dossiers: [['Nome', 'name'], ['Tipo', 'type'], ['Status', 'status'], ['Pendências', 'pendingChecklist']],
            certificates: [['Código público', 'publicCode'], ['Tipo', 'type'], ['Status', 'status'], ['Emissão', 'issuedAt']]
        };
        const cols = configs[kind] || configs.documents;
        return `
            <table class="document-table">
                <thead>
                    <tr>
                        ${cols.map(x => `<th>${esc(x[0])}</th>`).join('')}
                        <th>Ações</th>
                    </tr>
                </thead>
                <tbody>
                    ${rows.map(r => `
                        <tr>
                            ${cols.map(x => {
                                const val = r[x[1]];
                                if (x[1] === 'status') {
                                    const statusClass = (val || '').toLowerCase();
                                    return `<td><span class="badge ${statusClass}">${esc(val)}</span></td>`;
                                }
                                if (x[1] === 'currentVersion') {
                                    return `<td><strong>v${esc(val)}</strong></td>`;
                                }
                                if (x[1].endsWith('At')) {
                                    return `<td>${val ? new Date(val).toLocaleString('pt-BR') : '—'}</td>`;
                                }
                                return `<td>${esc(val)}</td>`;
                            }).join('')}
                            <td class="row-actions">${actions(r, kind)}</td>
                        </tr>
                    `).join('')}
                </tbody>
            </table>
        `;
    }

    function actions(r, k) {
        if (k === 'documents') {
            return `
                <button type="button" data-detail="${esc(r.id)}">Detalhes</button>
                <button type="button" data-version="${esc(r.id)}">Nova versão</button>
                <a href="${base}/api/documents/${esc(r.id)}/download" data-download="${esc(r.id)}">Baixar</a>
                ${r.status === 'ACTIVE' ? `<button type="button" data-archive="${esc(r.id)}">Arquivar</button>` : ''}
            `;
        }
        if (k === 'evidences' && r.status === 'PENDING') {
            return `
                <button type="button" data-evidence="${esc(r.id)}" data-status="VALIDATED">Validar</button>
                <button type="button" data-evidence="${esc(r.id)}" data-status="REJECTED">Rejeitar</button>
            `;
        }
        if (k === 'certificates' && r.status === 'ISSUED') {
            return `<button type="button" data-revoke="${esc(r.id)}">Revogar</button>`;
        }
        return '—';
    }

    async function load() {
        content.innerHTML = '<p class="empty-state">Carregando dados reais…</p>';
        if (pagination) pagination.innerHTML = '';
        try {
            if (view === 'dashboard') {
                const d = await api('/api/documents/dashboard');
                metrics.innerHTML = [
                    ['Documentos', d.totalDocuments],
                    ['Sem vínculo', d.unlinkedDocuments],
                    ['Evidências pendentes', d.pendingEvidences],
                    ['Validadas', d.validatedEvidences],
                    ['Rejeitadas', d.rejectedEvidences],
                    ['Dossiês em montagem', d.buildingDossiers],
                    ['Em revisão', d.reviewingDossiers],
                    ['Aprovados', d.approvedDossiers],
                    ['Certificados emitidos', d.issuedCertificates],
                    ['Revogados', d.revokedCertificates]
                ].map(x => `<article><small>${esc(x[0])}</small><strong>${Number(x[1]).toLocaleString('pt-BR')}</strong></article>`).join('');
                content.innerHTML = '<h2>Últimos uploads</h2>' + table(d.latestDocuments, 'documents') + '<h2>Últimos certificados</h2>' + table(d.latestCertificates, 'certificates');
                bind();
                return;
            }

            metrics.innerHTML = '';
            if (view === 'public') {
                content.innerHTML = `
                    <form id="public-search">
                        <h2>Consulta pública segura</h2>
                        <label>Código público <input name="code" required minlength="10" maxlength="40" autocomplete="off" placeholder="Ex: CERT-2026-ABCD"></label>
                        <button class="primary-button" type="submit">Verificar autenticidade</button>
                    </form>
                    <div id="public-result" style="margin-top:1rem;"></div>
                `;
                document.querySelector('#public-search').onsubmit = publicSearch;
                return;
            }

            let q = '';
            if (view === 'documents') {
                const search = encodeURIComponent(document.querySelector('#document-search').value.trim());
                const status = encodeURIComponent(document.querySelector('#document-status').value);
                const typeId = encodeURIComponent(typeFilter?.value || '');
                const params = [];
                if (search) params.push(`search=${search}`);
                if (status) params.push(`status=${status}`);
                if (typeId) params.push(`typeId=${typeId}`);
                if (params.length) q = '?' + params.join('&');
            } else if (view === 'evidences') {
                const status = encodeURIComponent(document.querySelector('#document-status').value);
                if (status) q = `?status=${status}`;
            }

            const rows = await api(`/api/${view}${q}`);
            content.innerHTML = table(rows, view);
            if (pagination && Array.isArray(rows)) {
                pagination.innerHTML = `<span>Total: <strong>${rows.length}</strong> registro(s)</span>`;
            }
            bind();
        } catch (e) {
            content.innerHTML = `<p class="empty-state">${esc(e.message)}</p>`;
        }
    }

    async function openDetail(id) {
        if (!detailDialog) return;
        try {
            const doc = await api(`/api/documents/${id}`);
            document.querySelector('#detail-title').textContent = doc.name;
            const badge = document.querySelector('#detail-badge');
            badge.textContent = doc.status;
            badge.className = `badge ${(doc.status || '').toLowerCase()}`;
            document.querySelector('#detail-type').textContent = doc.typeName || 'Geral';
            document.querySelector('#detail-current-version').textContent = `v${doc.Versions?.length || 1}`;
            document.querySelector('#detail-desc').textContent = doc.description || 'Nenhuma descrição informada.';
            document.querySelector('#detail-tags').textContent = (doc.tags || []).length ? doc.tags.join(', ') : '—';
            document.querySelector('#detail-links').textContent = (doc.links || []).length
                ? doc.links.map(l => `${l.entityType}: ${l.entityLabel}`).join(' · ')
                : 'Sem vínculos diretos';

            const dlBtn = document.querySelector('#detail-btn-download-latest');
            dlBtn.href = `${base}/api/documents/${doc.id}/download`;
            dlBtn.onclick = e => {
                e.preventDefault();
                downloadFile(doc.id, null, doc.name);
            };

            const nvBtn = document.querySelector('#detail-btn-new-version');
            nvBtn.onclick = () => {
                detailDialog.close();
                openAction('version', doc.id);
            };

            const archBtn = document.querySelector('#detail-btn-archive');
            if (doc.status === 'ACTIVE') {
                archBtn.style.display = 'inline-block';
                archBtn.onclick = async () => {
                    if (confirm(`Arquivar o documento "${doc.name}"?`)) {
                        await api(`/api/documents/${doc.id}/archive`, { method: 'POST' });
                        detailDialog.close();
                        load();
                    }
                };
            } else {
                archBtn.style.display = 'none';
            }

            const versionsContainer = document.querySelector('#detail-versions-table');
            if (doc.versions && doc.versions.length) {
                versionsContainer.innerHTML = `
                    <table class="document-table">
                        <thead>
                            <tr>
                                <th>Versão</th>
                                <th>Arquivo</th>
                                <th>Tamanho</th>
                                <th>SHA-256</th>
                                <th>Motivo</th>
                                <th>Data/Hora</th>
                                <th>Ação</th>
                            </tr>
                        </thead>
                        <tbody>
                            ${doc.versions.map(v => `
                                <tr>
                                    <td><strong>v${v.versionNumber}</strong></td>
                                    <td>${esc(v.originalName)}</td>
                                    <td>${(v.sizeBytes / 1024).toFixed(1)} KB</td>
                                    <td><span class="hash-code" title="${esc(v.sha256)}">${esc(v.sha256.substring(0, 10))}…</span></td>
                                    <td>${esc(v.changeReason || 'Upload inicial')}</td>
                                    <td>${new Date(v.createdAt).toLocaleString('pt-BR')}</td>
                                    <td>
                                        <button type="button" class="row-actions button" data-dl-version="${esc(v.id)}" data-doc-id="${esc(doc.id)}" data-file="${esc(v.originalName)}">Baixar</button>
                                    </td>
                                </tr>
                            `).join('')}
                        </tbody>
                    </table>
                `;
                versionsContainer.querySelectorAll('[data-dl-version]').forEach(b => {
                    b.onclick = () => downloadFile(b.dataset.docId, b.dataset.dlVersion, b.dataset.file);
                });
            } else {
                versionsContainer.innerHTML = '<p class="empty-state">Nenhuma versão anterior registrada.</p>';
            }

            detailDialog.showModal();
        } catch (e) {
            alert('Não foi possível carregar os detalhes do documento: ' + e.message);
        }
    }

    async function downloadFile(docId, versionId, filename = 'documento') {
        const query = versionId ? `?versionId=${encodeURIComponent(versionId)}` : '';
        const url = `${base}/api/documents/${docId}/download${query}`;
        try {
            const r = await fetch(url, { headers: headers(false) });
            if (!r.ok) throw new Error('Download não autorizado ou arquivo indisponível.');
            const disposition = r.headers.get('content-disposition');
            let downloadName = filename;
            if (disposition && disposition.includes('filename=')) {
                downloadName = disposition.split('filename=')[1].replace(/["']/g, '').trim();
            }
            const blob = await r.blob();
            const u = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = u;
            a.download = downloadName;
            document.body.appendChild(a);
            a.click();
            a.remove();
            URL.revokeObjectURL(u);
        } catch (e) {
            alert(e.message);
        }
    }

    function bind() {
        content.querySelectorAll('[data-detail]').forEach(b => {
            b.onclick = () => openDetail(b.dataset.detail);
        });

        content.querySelectorAll('[data-download]').forEach(a => {
            a.onclick = e => {
                e.preventDefault();
                downloadFile(a.dataset.download, null);
            };
        });

        content.querySelectorAll('[data-archive]').forEach(b => {
            b.onclick = async () => {
                if (confirm('Arquivar este documento operacional? O histórico e as versões serão preservados para auditoria.')) {
                    await api(`/api/documents/${b.dataset.archive}/archive`, { method: 'POST' });
                    load();
                }
            };
        });

        content.querySelectorAll('[data-version]').forEach(b => {
            b.onclick = () => openAction('version', b.dataset.version);
        });

        content.querySelectorAll('[data-evidence]').forEach(b => {
            b.onclick = () => openAction('evidence', b.dataset.evidence, b.dataset.status);
        });

        content.querySelectorAll('[data-revoke]').forEach(b => {
            b.onclick = () => openAction('revoke', b.dataset.revoke);
        });
    }

    function openAction(type, id, status) {
        action.dataset.type = type;
        action.dataset.id = id;
        action.dataset.status = status || '';
        action.querySelector('h2').textContent = type === 'version'
            ? 'Nova versão do documento'
            : type === 'revoke'
                ? 'Revogar certificado público'
                : `${status === 'VALIDATED' ? 'Validar' : 'Rejeitar'} evidência`;

        document.querySelector('#action-fields').innerHTML = type === 'version'
            ? `
                <label>Arquivo atualizado (até 25 MB)
                    <input name="file" type="file" required accept=".pdf,.png,.jpg,.jpeg,.webp,.csv,.txt,.xml,.docx,.xlsx">
                </label>
                <label>Motivo da alteração
                    <input name="reason" required minlength="3" maxlength="500" placeholder="Ex: Revisão de conformidade após análise">
                </label>
            `
            : `
                <label>Motivo${status === 'VALIDATED' ? ' (opcional)' : ''}
                    <textarea name="reason" ${status === 'REJECTED' || type === 'revoke' ? 'required' : ''} maxlength="500" placeholder="Justificativa da decisão…"></textarea>
                </label>
            `;
        action.querySelector('.form-error').textContent = '';
        action.showModal();
    }

    action.querySelector('form').onsubmit = async e => {
        e.preventDefault();
        if (!e.target.reportValidity()) return;
        const { type, id, status } = action.dataset;
        const f = new FormData(e.target);
        const err = e.target.querySelector('.form-error');
        err.textContent = '';
        try {
            if (type === 'version') {
                const file = f.get('file');
                if (file && file.size > 26214400) {
                    err.textContent = 'O arquivo deve ter no máximo 25 MB.';
                    return;
                }
                await api(`/api/documents/${id}/versions`, { method: 'POST', body: f });
            } else if (type === 'evidence') {
                await api(`/api/evidences/${id}/decision`, {
                    method: 'POST',
                    body: JSON.stringify({ status, reason: f.get('reason') || null })
                });
            } else {
                await api(`/api/certificates/${id}/revoke`, {
                    method: 'POST',
                    body: JSON.stringify({ reason: f.get('reason') })
                });
            }
            action.close();
            load();
        } catch (x) {
            err.textContent = x.message;
        }
    };

    async function publicSearch(e) {
        e.preventDefault();
        const box = document.querySelector('#public-result');
        box.innerHTML = '<p>Verificando autenticidade do certificado…</p>';
        try {
            const c = await api(`/api/certificates/public/${encodeURIComponent(new FormData(e.target).get('code'))}`);
            box.innerHTML = `
                <article style="border: 1px solid rgba(45,191,127,0.3); border-radius:12px; padding:1.2rem; background:rgba(12,34,27,0.85);">
                    <header style="display:flex; justify-content:space-between; align-items:center; margin-bottom:0.75rem;">
                        <h3 style="margin:0;">${esc(c.type)}</h3>
                        <span class="badge ${c.status === 'ISSUED' ? '' : 'rejected'}">${esc(c.status)}</span>
                    </header>
                    <p><strong>Organização:</strong> ${esc(c.organizationName)}</p>
                    <p>${esc(c.subjectSummary)}</p>
                    <p>${esc(c.traceabilitySummary)}</p>
                    <small style="color:#8cd2af;">Hash imutável: <code class="hash-code">${esc(c.verificationHash)}</code></small>
                </article>
            `;
        } catch (x) {
            box.innerHTML = `<p class="form-error">${esc(x.message)}</p>`;
        }
    }

    document.querySelectorAll('[data-view]').forEach(b => {
        b.onclick = () => {
            document.querySelectorAll('[data-view]').forEach(x => x.classList.remove('active'));
            b.classList.add('active');
            view = b.dataset.view;
            const toolbar = document.querySelector('.document-toolbar');
            toolbar.hidden = view === 'dashboard' || view === 'public';
            if (typeFilter) typeFilter.style.display = view === 'documents' ? 'inline-block' : 'none';
            load();
        };
    });

    document.querySelector('#new-document').onclick = async () => {
        const types = await api('/api/documents/types');
        const s = upload.querySelector('[name=documentTypeId]');
        s.length = 1;
        types.forEach(x => s.add(new Option(x.label, x.id)));
        upload.querySelector('.form-error').textContent = '';
        upload.showModal();
    };

    upload.querySelector('[name=entityType]').onchange = async e => {
        const s = upload.querySelector('[name=entityId]');
        s.length = 0;
        if (!e.target.value) {
            s.disabled = true;
            s.add(new Option('Selecione o tipo primeiro', ''));
            return;
        }
        s.disabled = true;
        s.add(new Option('Carregando…', ''));
        try {
            const rows = await api(`/api/documents/lookups/${e.target.value}`);
            s.length = 0;
            s.add(new Option('Selecione…', ''));
            rows.forEach(x => s.add(new Option(x.label, x.id)));
            s.disabled = false;
        } catch (x) {
            s.length = 0;
            s.add(new Option(x.message, ''));
        }
    };

    upload.querySelector('form').onsubmit = async e => {
        e.preventDefault();
        if (!e.target.reportValidity()) return;
        const f = new FormData(e.target);
        const file = f.get('file');
        const error = e.target.querySelector('.form-error');
        error.textContent = '';
        if (file && file.size > 26214400) {
            error.textContent = 'O arquivo deve ter no máximo 25 MB.';
            return;
        }
        if (!f.get('entityType')) {
            f.delete('entityType');
            f.delete('entityId');
        }
        try {
            await api('/api/documents', { method: 'POST', body: f });
            upload.close();
            e.target.reset();
            view = 'documents';
            document.querySelectorAll('[data-view]').forEach(x => x.classList.toggle('active', x.dataset.view === 'documents'));
            document.querySelector('.document-toolbar').hidden = false;
            load();
        } catch (x) {
            error.textContent = x.message;
        }
    };

    document.querySelectorAll('dialog [data-close]').forEach(b => {
        b.onclick = () => b.closest('dialog').close();
    });

    let timer;
    document.querySelector('#document-search').oninput = () => {
        clearTimeout(timer);
        timer = setTimeout(load, 300);
    };
    document.querySelector('#document-status').onchange = load;
    if (typeFilter) typeFilter.onchange = load;

    document.querySelector('#export-csv').onclick = () => {
        if (['documents', 'evidences', 'dossiers', 'certificates'].includes(view)) {
            location.href = `${base}/api/documents/export/${view}`;
        }
    };

    loadDocumentTypes();
    load();
})();
