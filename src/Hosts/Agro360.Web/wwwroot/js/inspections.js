(() => {
  const api = document.querySelector('meta[name="api-base"]')?.content?.replace(/\/$/, '') || '';
  const token = () => localStorage.getItem('agro360.accessToken');
  const headers = () => ({ 'Content-Type': 'application/json', Authorization: `Bearer ${token()}` });
  const content = document.querySelector('#inspections-content');
  const statusEl = document.querySelector('#inspections-status');
  const dialog = document.querySelector('#insp-dialog');
  const form = document.querySelector('#insp-form');
  const fields = document.querySelector('#insp-fields');
  const primaryBtn = document.querySelector('#inspections-primary');
  const processSel = document.querySelector('#insp-process');
  const statusSel = document.querySelector('#insp-status');
  const searchInput = document.querySelector('#insp-search');

  async function askConfirm(msg, title = 'Confirme a operação') {
    if (window.agro360Feedback?.confirm) {
      const res = await window.agro360Feedback.confirm({ title, message: msg, confirmText: 'Confirmar', cancelText: 'Cancelar' });
      return Boolean(res?.confirmed ?? res);
    }
    return window.confirm(msg);
  }

  function notifyError(msg, title = 'Inspeção de Qualidade') {
    if (window.agro360Feedback?.toast) {
      window.agro360Feedback.toast('error', title, msg);
    } else if (window.toastError) {
      window.toastError(title, msg);
    }
  }

  const PROCESS_OPTS = [
    ['PURCHASE_RECEIPT', 'Recebimento de compra'],
    ['HARVEST_RECEIPT', 'Recebimento de colheita'],
    ['PRODUCTION', 'Produção'],
    ['STORAGE', 'Armazenagem'],
    ['SHIPMENT', 'Expedição'],
    ['RETURN', 'Devolução']
  ];
  const CRITERION_TYPES = [
    ['PASS_FAIL', 'Passa / Falha'],
    ['SINGLE_CHOICE', 'Escolha única'],
    ['MULTI_CHOICE', 'Múltipla escolha'],
    ['TEXT', 'Texto'],
    ['NUMBER', 'Número'],
    ['DATE', 'Data'],
    ['DOCUMENT_EVIDENCE', 'Evidência documental']
  ];
  const STATUS_FILTERS = {
    models: [['ACTIVE', 'Ativo'], ['INACTIVE', 'Inativo']],
    runs: [['IN_PROGRESS', 'Em andamento'], ['PENDING_REVIEW', 'Aguardando revisão'], ['COMPLETED', 'Concluída'], ['CANCELLED', 'Cancelada']],
    schedules: [['ACTIVE', 'Ativa'], ['INACTIVE', 'Inativa']],
    intents: [
      ['STARTED', 'Iniciada'],
      ['PENDING_MODEL', 'Sem modelo'],
      ['AMBIGUOUS', 'Ambiguidade'],
      ['SKIPPED_NO_ACTOR', 'Sem operador'],
      ['PENDING', 'Pendente']
    ],
    execution: [],
    result: []
  };

  let view = 'models';
  let models = [];
  let runs = [];
  let schedules = [];
  let intents = [];
  let currentModel = null;
  let currentVersion = null;
  let currentRun = null;
  let lastCompleteResult = null;
  let mobileSection = 0;
  let busy = false;
  let draftDirty = false;

  const INTENT_STATUS_LABELS = {
    STARTED: 'Iniciada',
    PENDING_MODEL: 'Sem modelo',
    AMBIGUOUS: 'Ambiguidade',
    SKIPPED_NO_ACTOR: 'Ignorado (sem operador)',
    PENDING: 'Pendente'
  };

  const esc = v => String(v ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const fmt = iso => {
    if (!iso) return '—';
    try { return new Date(iso).toLocaleString('pt-BR'); } catch { return esc(iso); }
  };
  const session = () => { try { return JSON.parse(localStorage.getItem('agro360.session') || 'null'); } catch { return null; } };
  const processLabel = code => PROCESS_OPTS.find(x => x[0] === code)?.[1] || code || '—';
  const typeLabel = t => CRITERION_TYPES.find(x => x[0] === t)?.[1] || t || '—';
  const intentStatusLabel = s => INTENT_STATUS_LABELS[String(s || '').toUpperCase()] || s || '—';
  const pill = (s) => `<span class="status-pill status-${esc(String(s || '').toLowerCase())}">${esc(s || '—')}</span>`;
  const uid = () => (crypto.randomUUID ? crypto.randomUUID() : `k-${Date.now()}-${Math.random().toString(16).slice(2)}`);
  const setStatus = (msg, done = false) => { statusEl.textContent = msg || ''; if (done) busy = false; };
  const finishLoading = (msg = '') => setStatus(msg, true);

  async function request(path, options = {}) {
    const r = await fetch(api + path, { ...options, headers: { ...headers(), ...(options.headers || {}) } });
    if (r.status === 204) return null;
    const body = await r.json().catch(() => ({}));
    if (!r.ok) {
      const err = new Error(body.detail || body.title || `Falha HTTP ${r.status}`);
      err.status = r.status;
      err.body = body;
      throw err;
    }
    return body;
  }

  async function fillLookup(select, resource) {
    try {
      const d = await request(`/api/lookups/${resource}?pageSize=100`);
      const keep = select.value;
      select.innerHTML = '<option value="">Selecione…</option>';
      (d.items || []).forEach(x => select.add(new Option(x.label || x.name, x.id)));
      if (keep) select.value = keep;
    } catch {
      select.closest('label')?.insertAdjacentHTML('beforeend', ' <small class="hint">Lista indisponível</small>');
    }
  }

  function syncStatusFilter() {
    const opts = STATUS_FILTERS[view] || [];
    const prev = statusSel.value;
    statusSel.innerHTML = '<option value="">Todos</option>' + opts.map(([v, l]) => `<option value="${v}">${l}</option>`).join('');
    statusSel.value = opts.some(o => o[0] === prev) ? prev : '';
    statusSel.closest('label').hidden = opts.length === 0;
    processSel.closest('label').hidden = view === 'execution' || view === 'result';
    searchInput.closest('label').hidden = view === 'execution' || view === 'result';
  }

  function syncPrimary() {
    const map = { models: 'Novo modelo', runs: 'Iniciar inspeção', schedules: 'Nova programação', execution: 'Salvar rascunho', result: 'Voltar às inspeções', intents: 'Atualizar' };
    primaryBtn.textContent = map[view] || 'Ação';
    primaryBtn.hidden = false;
  }

  function setView(next, opts = {}) {
    view = next;
    document.querySelectorAll('.inspections-tabs button').forEach(b => b.classList.toggle('active', b.dataset.view === view));
    syncStatusFilter();
    syncPrimary();
    if (!opts.skipLoad) load();
  }

  async function load() {
    busy = true;
    setStatus('Carregando…');
    content.innerHTML = '';
    try {
      if (view === 'models') await loadModels();
      else if (view === 'runs') await loadRuns();
      else if (view === 'execution') renderExecution();
      else if (view === 'result') renderResult();
      else if (view === 'schedules') await loadSchedules();
      else if (view === 'intents') await loadIntents();
      finishLoading();
    } catch (e) {
      finishLoading(e.message);
      content.innerHTML = `<div class="empty-state"><h2>Não foi possível carregar</h2><p>${esc(e.message)}</p></div>`;
    }
  }

  function qMatch(obj) {
    const q = searchInput.value.trim().toLowerCase();
    if (!q) return true;
    return JSON.stringify(obj).toLowerCase().includes(q);
  }

  // ——— Models ———
  async function loadModels() {
    const qs = new URLSearchParams();
    if (processSel.value) qs.set('process', processSel.value);
    if (statusSel.value) qs.set('status', statusSel.value);
    models = await request(`/api/inspections/models?${qs}`) || [];
    if (!Array.isArray(models)) models = models.items || [];
    const filtered = models.filter(qMatch);
    if (currentModel) {
      renderModelDetail(currentModel);
      return;
    }
    content.innerHTML = filtered.length
      ? `<div class="insp-list">${filtered.map(m => `
          <article class="data-card actionable" tabindex="0" role="button" data-model="${esc(m.id)}">
            <div><strong>${esc(m.code)} · ${esc(m.name)}</strong>
              <small>${esc(processLabel(m.processCode))} · v publicada ${esc(m.publishedVersionNumber || 0)} · atualizado ${fmt(m.updatedAt)}</small></div>
            ${pill(m.status)}
          </article>`).join('')}</div>`
      : `<div class="empty-state"><h2>Nenhum modelo</h2><p>Cadastre o primeiro modelo para o processo desejado.</p></div>`;
    content.querySelectorAll('[data-model]').forEach(el => {
      const open = () => openModel(el.dataset.model);
      el.onclick = open;
      el.onkeydown = e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); open(); } };
    });
  }

  async function openModel(id) {
    setStatus('Carregando modelo…');
    currentModel = await request(`/api/inspections/models/${id}`);
    currentVersion = null;
    renderModelDetail(currentModel);
    finishLoading();
  }

  function renderModelDetail(m) {
    const versions = m.versions || [];
    const draft = versions.find(v => String(v.status).toUpperCase() === 'DRAFT');
    const inReview = versions.find(v => String(v.status).toUpperCase() === 'IN_REVIEW');
    content.innerHTML = `
      <div class="insp-panel">
        <header class="insp-section" style="border:0;padding:0;background:transparent">
          <div>
            <button type="button" class="ghost-button" data-back-models>← Voltar</button>
            <h2>${esc(m.code)} · ${esc(m.name)}</h2>
            <p>${esc(m.description || 'Sem descrição.')}</p>
            <div class="insp-summary">
              <article class="metric-chip"><small>Processo</small><strong>${esc(processLabel(m.processCode))}</strong></article>
              <article class="metric-chip"><small>Status</small><strong>${pill(m.status)}</strong></article>
              <article class="metric-chip"><small>Precedência</small><strong>${esc(m.precedence)}</strong></article>
              <article class="metric-chip"><small>Seleção manual</small><strong>${m.allowManualSelection ? 'Permitida' : 'Bloqueada'}</strong></article>
            </div>
          </div>
        </header>
        <div class="insp-actions">
          <button type="button" data-edit-meta>Editar metadados</button>
          ${draft ? `<button type="button" class="primary-button" data-edit-draft="${esc(draft.id)}">Editar rascunho v${esc(draft.versionNumber)}</button>` : `<button type="button" class="primary-button" data-new-draft>Novo rascunho</button>`}
          ${inReview ? `<button type="button" data-publish="${esc(inReview.id)}" data-rv="${esc(inReview.rowVersion)}">Publicar v${esc(inReview.versionNumber)}</button>` : ''}
          ${String(m.status).toUpperCase() !== 'INACTIVE' ? `<button type="button" data-inactivate-model>Inativar modelo</button>` : ''}
        </div>
        <section class="insp-section"><h3>Versões</h3>
          <div class="insp-list">${versions.map(v => `
            <article class="data-card">
              <div><strong>v${esc(v.versionNumber)}</strong>
                <small>Vigência ${esc(v.validFrom || '—')} → ${esc(v.validUntil || 'aberta')} · ${esc(v.changeReason || 'sem motivo')}</small></div>
              <div class="insp-actions">
                ${pill(v.status)}
                ${String(v.status).toUpperCase() === 'DRAFT' ? `<button type="button" data-edit-draft="${esc(v.id)}">Editar</button><button type="button" data-submit-review="${esc(v.id)}" data-rv="${esc(v.rowVersion)}">Enviar revisão</button>` : ''}
                ${String(v.status).toUpperCase() === 'IN_REVIEW' ? `<button type="button" data-publish="${esc(v.id)}" data-rv="${esc(v.rowVersion)}">Publicar</button>` : ''}
                ${['PUBLISHED', 'DRAFT', 'IN_REVIEW'].includes(String(v.status).toUpperCase()) ? '' : ''}
                ${String(v.status).toUpperCase() !== 'INACTIVE' && String(v.status).toUpperCase() !== 'SUPERSEDED' ? `<button type="button" data-inactivate-version="${esc(v.id)}">Inativar</button>` : ''}
              </div>
            </article>`).join('') || '<p class="notice">Nenhuma versão ainda.</p>'}
          </div>
        </section>
        <p class="notice">Conteúdo publicado é imutável. Edite apenas rascunhos (DRAFT). Critérios críticos não podem ser compensados por peso; respostas N/A ficam fora do score.</p>
      </div>`;
    content.querySelector('[data-back-models]').onclick = () => { currentModel = null; load(); };
    content.querySelector('[data-edit-meta]')?.addEventListener('click', () => openModelMetaForm(m));
    content.querySelector('[data-new-draft]')?.addEventListener('click', () => createDraft(m.id));
    content.querySelectorAll('[data-edit-draft]').forEach(b => b.onclick = () => openDraftEditor(m.id, b.dataset.editDraft));
    content.querySelectorAll('[data-submit-review]').forEach(b => b.onclick = () => submitReview(b.dataset.submitReview, Number(b.dataset.rv)));
    content.querySelectorAll('[data-publish]').forEach(b => b.onclick = () => publishVersion(b.dataset.publish, Number(b.dataset.rv)));
    content.querySelector('[data-inactivate-model]')?.addEventListener('click', () => inactivateEntity('model', m.id));
    content.querySelectorAll('[data-inactivate-version]').forEach(b => b.onclick = () => inactivateEntity('version', b.dataset.inactivateVersion));
  }

  function openModelMetaForm(existing) {
    document.querySelector('#insp-form-title').textContent = existing ? 'Editar modelo' : 'Novo modelo';
    fields.innerHTML = `
      ${existing ? '' : `<label>Código <small class="hint">Único no tenant</small><input name="code" required maxlength="40" /></label>`}
      <label class="${existing ? 'wide' : ''}">Nome <input name="name" required maxlength="180" value="${esc(existing?.name || '')}" /></label>
      <label class="wide">Descrição <textarea name="description" maxlength="2000">${esc(existing?.description || '')}</textarea></label>
      ${existing ? '' : `<label>Processo <select name="processCode" required>${PROCESS_OPTS.map(([v, l]) => `<option value="${v}">${l}</option>`).join('')}</select></label>`}
      <label>Precedência <small class="hint">Menor valor vence em empate de especificidade</small><input name="precedence" type="number" min="1" required value="${esc(existing?.precedence ?? 100)}" /></label>
      <label>Seleção manual <select name="allowManualSelection"><option value="false" ${!existing?.allowManualSelection ? 'selected' : ''}>Não</option><option value="true" ${existing?.allowManualSelection ? 'selected' : ''}>Sim</option></select></label>
      <label>Unidade <select name="unitId" data-resource="units"><option value="">Opcional…</option></select></label>
      <label>Categoria de produto <input name="productCategory" maxlength="80" value="${esc(existing?.productCategory || '')}" /></label>
      <label>Produto <select name="productId" data-resource="products"><option value="">Opcional…</option></select></label>
      <label>Responsável revisão <select name="reviewResponsibleId" data-resource="users"><option value="">Opcional…</option></select></label>
      <label class="wide">Instruções <textarea name="instructions" maxlength="4000">${esc(existing?.instructions || '')}</textarea></label>
      <input type="hidden" name="expectedRowVersion" value="${esc(existing?.rowVersion ?? '')}" />`;
    form.dataset.mode = existing ? 'update-model' : 'create-model';
    form.dataset.id = existing?.id || '';
    dialog.showModal();
    Promise.all([...fields.querySelectorAll('select[data-resource]')].map(s => fillLookup(s, s.dataset.resource))).then(() => {
      if (existing?.unitId) fields.querySelector('[name=unitId]').value = existing.unitId;
      if (existing?.productId) fields.querySelector('[name=productId]').value = existing.productId;
      if (existing?.reviewResponsibleId) fields.querySelector('[name=reviewResponsibleId]').value = existing.reviewResponsibleId;
    });
  }

  async function createDraft(modelId) {
    if (!await askConfirm('Criar novo rascunho a partir da versão publicada mais recente (se houver)?')) return;
    try {
      setStatus('Criando rascunho…');
      const from = (currentModel?.versions || []).find(v => String(v.status).toUpperCase() === 'PUBLISHED');
      const res = await request(`/api/inspections/models/${modelId}/versions`, {
        method: 'POST',
        body: JSON.stringify({ fromVersionId: from?.id || null, changeReason: 'Novo rascunho' })
      });
      const versionId = res?.id || res;
      await openModel(modelId);
      await openDraftEditor(modelId, versionId);
    } catch (e) { finishLoading(e.message); }
  }

  async function openDraftEditor(modelId, versionId) {
    setStatus('Carregando rascunho…');
    try {
      currentVersion = await request(`/api/inspections/versions/${versionId}`);
    } catch {
      currentVersion = {
        id: versionId,
        modelId,
        versionNumber: '—',
        status: 'DRAFT',
        changeReason: '',
        validFrom: '',
        validUntil: '',
        rowVersion: 1,
        sections: [{ id: null, stableKey: 'sec-1', name: 'Seção 1', description: '', sortOrder: 1, criteria: [blankCriterion(1)] }]
      };
    }
    if (String(currentVersion.status || 'DRAFT').toUpperCase() !== 'DRAFT') {
      content.innerHTML = `<div class="notice danger" role="alert">Esta versão está ${esc(currentVersion.status)} e não pode ser editada. Conteúdo publicado é imutável.</div>
        <button type="button" class="ghost-button" data-back>Voltar</button>`;
      content.querySelector('[data-back]').onclick = () => openModel(modelId);
      finishLoading();
      return;
    }
    mobileSection = 0;
    draftDirty = false;
    renderDraftEditor();
    finishLoading();
  }

  function blankCriterion(order) {
    return {
      id: null, stableKey: `c-${order}`, name: '', guidance: '', criterionType: 'PASS_FAIL',
      required: true, critical: false, allowNotApplicable: false, requireNaJustification: false,
      requireReview: false, unit: '', weight: null, sortOrder: order, options: [],
      approval: { expectedPass: true, expectedNumber: '', expectedNumberMax: '', expectedText: '', expectedChoices: '' }
    };
  }

  function normalizeDraft(v) {
    const sections = (v.sections || []).map((s, si) => ({
      id: s.id || null,
      stableKey: s.stableKey || `sec-${si + 1}`,
      name: s.name || `Seção ${si + 1}`,
      description: s.description || '',
      sortOrder: s.sortOrder ?? si + 1,
      criteria: (s.criteria || []).map((c, ci) => {
        let approval = c.approval || {};
        if (!c.approval && c.approvalConditionJson) {
          try { approval = JSON.parse(c.approvalConditionJson) || {}; } catch { approval = {}; }
        }
        return {
          id: c.id || null,
          stableKey: c.stableKey || `c-${si + 1}-${ci + 1}`,
          name: c.name || '',
          guidance: c.guidance || '',
          criterionType: c.criterionType || 'PASS_FAIL',
          required: !!c.required,
          critical: !!c.critical,
          allowNotApplicable: !!c.allowNotApplicable,
          requireNaJustification: !!c.requireNaJustification,
          requireReview: !!c.requireReview,
          unit: c.unit || '',
          weight: c.weight ?? null,
          sortOrder: c.sortOrder ?? ci + 1,
          options: (c.options || []).map((o, oi) => ({ value: o.value, label: o.label, sortOrder: o.sortOrder ?? oi + 1 })),
          approval: {
            expectedPass: approval.expectedPass ?? true,
            expectedNumber: approval.expectedNumber ?? approval.min ?? '',
            expectedNumberMax: approval.expectedNumberMax ?? approval.max ?? '',
            expectedText: approval.expectedText || '',
            expectedChoices: Array.isArray(approval.expectedChoices) ? approval.expectedChoices.join(', ') : (approval.expectedChoices || '')
          }
        };
      })
    }));
    if (!sections.length) sections.push({ id: null, stableKey: 'sec-1', name: 'Seção 1', description: '', sortOrder: 1, criteria: [blankCriterion(1)] });
    v.sections = sections;
    return v;
  }

  function renderDraftEditor() {
    currentVersion = normalizeDraft(currentVersion);
    const sections = currentVersion.sections;
    const isMobile = window.matchMedia('(max-width:750px)').matches;
    content.innerHTML = `
      <div class="insp-panel" id="draft-editor">
        <button type="button" class="ghost-button" data-back-model>← Voltar ao modelo</button>
        <h2>Rascunho v${esc(currentVersion.versionNumber)}</h2>
        <p class="notice">Somente DRAFT é editável. Peso é opcional: N/A excluído do cálculo; falha crítica não pode ser compensada por peso.</p>
        <form id="draft-meta" class="form-grid">
          <label class="wide">Motivo da alteração <input name="changeReason" maxlength="2000" value="${esc(currentVersion.changeReason || '')}" /></label>
          <label>Vigência início <input name="validFrom" type="date" value="${esc(currentVersion.validFrom || '')}" /></label>
          <label>Vigência fim <input name="validUntil" type="date" value="${esc(currentVersion.validUntil || '')}" /></label>
        </form>
        <div class="section-nav mobile-only">
          <button type="button" data-prev-sec ${mobileSection <= 0 ? 'disabled' : ''}>Anterior</button>
          <span>Seção ${mobileSection + 1} de ${sections.length}</span>
          <button type="button" data-next-sec ${mobileSection >= sections.length - 1 ? 'disabled' : ''}>Próxima</button>
        </div>
        <div id="draft-sections">${sections.map((s, si) => sectionEditorHtml(s, si, isMobile)).join('')}</div>
        <div class="insp-actions">
          <button type="button" data-add-section>Adicionar seção</button>
          <button type="button" class="primary-button" data-save-draft>Salvar rascunho</button>
          <button type="button" data-submit-from-editor>Enviar para revisão</button>
        </div>
        <p class="saved-at" data-draft-saved></p>
      </div>`;
    bindDraftEditor();
  }

  function sectionEditorHtml(s, si, isMobile) {
    const hidden = isMobile && si !== mobileSection;
    return `<section class="insp-section" data-section-index="${si}" data-mobile-hidden="${hidden}">
      <header>
        <div class="form-grid" style="flex:1;width:100%">
          <label>Chave estável <input data-sec="stableKey" value="${esc(s.stableKey)}" maxlength="80" required /></label>
          <label>Nome da seção <input data-sec="name" value="${esc(s.name)}" maxlength="200" required /></label>
          <label class="wide">Descrição <textarea data-sec="description" maxlength="2000">${esc(s.description)}</textarea></label>
        </div>
        <button type="button" class="ghost-button" data-remove-section title="Remover seção">×</button>
      </header>
      ${(s.criteria || []).map((c, ci) => criterionEditorHtml(c, si, ci)).join('')}
      <button type="button" data-add-criterion>Adicionar critério</button>
    </section>`;
  }

  function criterionEditorHtml(c, si, ci) {
    const needsOpts = c.criterionType === 'SINGLE_CHOICE' || c.criterionType === 'MULTI_CHOICE';
    const isNum = c.criterionType === 'NUMBER';
    const isPf = c.criterionType === 'PASS_FAIL';
    const isText = c.criterionType === 'TEXT';
    return `<article class="criterion-card ${c.critical ? 'critical' : ''}" data-crit-si="${si}" data-crit-ci="${ci}">
      <div class="form-grid">
        <label>Chave <input data-crit="stableKey" value="${esc(c.stableKey)}" maxlength="80" required /></label>
        <label>Nome <input data-crit="name" value="${esc(c.name)}" maxlength="200" required /></label>
        <label>Tipo <select data-crit="criterionType">${CRITERION_TYPES.map(([v, l]) => `<option value="${v}" ${c.criterionType === v ? 'selected' : ''}>${l}</option>`).join('')}</select></label>
        <label>Unidade <input data-crit="unit" value="${esc(c.unit)}" maxlength="30" ${isNum ? '' : ''} /></label>
        <label class="wide">Orientação <textarea data-crit="guidance" maxlength="2000">${esc(c.guidance)}</textarea></label>
        <label><input type="checkbox" data-crit="required" ${c.required ? 'checked' : ''} /> Obrigatório</label>
        <label><input type="checkbox" data-crit="critical" ${c.critical ? 'checked' : ''} /> Crítico</label>
        <label><input type="checkbox" data-crit="allowNotApplicable" ${c.allowNotApplicable ? 'checked' : ''} /> Permite N/A</label>
        <label><input type="checkbox" data-crit="requireNaJustification" ${c.requireNaJustification ? 'checked' : ''} /> Justificativa N/A</label>
        <label><input type="checkbox" data-crit="requireReview" ${c.requireReview ? 'checked' : ''} /> Exige revisão / evidência</label>
        <label>Peso <small class="hint">Opcional; N/A fora do score; crítico não compensável</small><input data-crit="weight" type="number" min="0.0001" step="0.0001" value="${esc(c.weight ?? '')}" /></label>
        ${isPf ? `<label>Esperado PASS/FAIL <select data-appr="expectedPass"><option value="true" ${c.approval.expectedPass !== false ? 'selected' : ''}>PASS</option><option value="false" ${c.approval.expectedPass === false ? 'selected' : ''}>FAIL</option></select></label>` : ''}
        ${isNum ? `<label>Mínimo aprovado <input data-appr="expectedNumber" type="number" step="any" value="${esc(c.approval.expectedNumber)}" /></label>
                   <label>Máximo aprovado <input data-appr="expectedNumberMax" type="number" step="any" value="${esc(c.approval.expectedNumberMax)}" /></label>` : ''}
        ${isText ? `<label class="wide">Texto esperado (opcional) <input data-appr="expectedText" value="${esc(c.approval.expectedText)}" /></label>` : ''}
        ${needsOpts ? `<label class="wide">Valores aprovados (vírgula) <input data-appr="expectedChoices" value="${esc(c.approval.expectedChoices)}" /></label>
          <fieldset><legend>Opções</legend><div class="options-editor" data-options>
            ${(c.options || []).map((o, oi) => `<div class="opt-row"><label>Valor<input data-opt-value value="${esc(o.value)}" required /></label><label>Rótulo<input data-opt-label value="${esc(o.label)}" required /></label><button type="button" data-remove-opt>×</button></div>`).join('')}
          </div><button type="button" data-add-opt>Adicionar opção</button></fieldset>` : ''}
      </div>
      <div class="insp-actions"><button type="button" data-remove-criterion>Remover critério</button></div>
    </article>`;
  }

  function readDraftFromDom() {
    const meta = content.querySelector('#draft-meta');
    currentVersion.changeReason = meta.changeReason.value || null;
    currentVersion.validFrom = meta.validFrom.value || null;
    currentVersion.validUntil = meta.validUntil.value || null;
    content.querySelectorAll('[data-section-index]').forEach(secEl => {
      const si = Number(secEl.dataset.sectionIndex);
      const s = currentVersion.sections[si];
      s.stableKey = secEl.querySelector('[data-sec=stableKey]').value.trim();
      s.name = secEl.querySelector('[data-sec=name]').value.trim();
      s.description = secEl.querySelector('[data-sec=description]').value.trim();
      s.sortOrder = si + 1;
      secEl.querySelectorAll('[data-crit-ci]').forEach(card => {
        const ci = Number(card.dataset.critCi);
        const c = s.criteria[ci];
        card.querySelectorAll('[data-crit]').forEach(inp => {
          const key = inp.dataset.crit;
          if (inp.type === 'checkbox') c[key] = inp.checked;
          else if (key === 'weight') c.weight = inp.value === '' ? null : Number(inp.value);
          else c[key] = inp.value;
        });
        card.querySelectorAll('[data-appr]').forEach(inp => {
          const key = inp.dataset.appr;
          if (key === 'expectedPass') c.approval.expectedPass = inp.value === 'true';
          else c.approval[key] = inp.value;
        });
        const opts = [];
        card.querySelectorAll('.opt-row').forEach((row, oi) => {
          opts.push({
            value: row.querySelector('[data-opt-value]').value.trim(),
            label: row.querySelector('[data-opt-label]').value.trim(),
            sortOrder: oi + 1
          });
        });
        c.options = opts;
      });
    });
  }

  function bindDraftEditor() {
    const root = content.querySelector('#draft-editor');
    root.querySelector('[data-back-model]').onclick = () => { currentVersion = null; openModel(currentModel.id); };
    root.querySelector('[data-add-section]').onclick = () => {
      readDraftFromDom();
      const n = currentVersion.sections.length + 1;
      currentVersion.sections.push({ id: null, stableKey: `sec-${n}`, name: `Seção ${n}`, description: '', sortOrder: n, criteria: [blankCriterion(1)] });
      mobileSection = currentVersion.sections.length - 1;
      renderDraftEditor();
    };
    root.querySelectorAll('[data-remove-section]').forEach(btn => btn.onclick = () => {
      readDraftFromDom();
      if (currentVersion.sections.length <= 1) { notifyError('Mantenha ao menos uma seção.'); return; }
      const si = Number(btn.closest('[data-section-index]').dataset.sectionIndex);
      currentVersion.sections.splice(si, 1);
      mobileSection = Math.min(mobileSection, currentVersion.sections.length - 1);
      renderDraftEditor();
    });
    root.querySelectorAll('[data-add-criterion]').forEach(btn => btn.onclick = () => {
      readDraftFromDom();
      const si = Number(btn.closest('[data-section-index]').dataset.sectionIndex);
      const list = currentVersion.sections[si].criteria;
      list.push(blankCriterion(list.length + 1));
      renderDraftEditor();
    });
    root.querySelectorAll('[data-remove-criterion]').forEach(btn => btn.onclick = () => {
      readDraftFromDom();
      const card = btn.closest('[data-crit-ci]');
      const si = Number(card.dataset.critSi);
      const ci = Number(card.dataset.critCi);
      if (currentVersion.sections[si].criteria.length <= 1) { notifyError('Cada seção precisa de ao menos um critério.'); return; }
      currentVersion.sections[si].criteria.splice(ci, 1);
      renderDraftEditor();
    });
    root.querySelectorAll('[data-crit=criterionType]').forEach(sel => sel.onchange = () => { readDraftFromDom(); renderDraftEditor(); });
    root.querySelectorAll('[data-add-opt]').forEach(btn => btn.onclick = () => {
      readDraftFromDom();
      const card = btn.closest('[data-crit-ci]');
      const c = currentVersion.sections[Number(card.dataset.critSi)].criteria[Number(card.dataset.critCi)];
      c.options.push({ value: '', label: '', sortOrder: c.options.length + 1 });
      renderDraftEditor();
    });
    root.querySelectorAll('[data-remove-opt]').forEach(btn => btn.onclick = () => {
      readDraftFromDom();
      const row = btn.closest('.opt-row');
      const card = btn.closest('[data-crit-ci]');
      const idx = [...row.parentElement.querySelectorAll('.opt-row')].indexOf(row);
      currentVersion.sections[Number(card.dataset.critSi)].criteria[Number(card.dataset.critCi)].options.splice(idx, 1);
      renderDraftEditor();
    });
    root.querySelector('[data-prev-sec]')?.addEventListener('click', () => { readDraftFromDom(); mobileSection = Math.max(0, mobileSection - 1); renderDraftEditor(); });
    root.querySelector('[data-next-sec]')?.addEventListener('click', () => { readDraftFromDom(); mobileSection = Math.min(currentVersion.sections.length - 1, mobileSection + 1); renderDraftEditor(); });
    root.querySelector('[data-save-draft]').onclick = () => saveDraft(false);
    root.querySelector('[data-submit-from-editor]').onclick = () => saveDraft(true);
    draftDirty = true;
  }

  function buildApprovalJson(c) {
    const a = c.approval || {};
    const obj = {};
    if (c.criterionType === 'PASS_FAIL') obj.expectedPass = a.expectedPass !== false;
    if (c.criterionType === 'NUMBER') {
      if (a.expectedNumber !== '' && a.expectedNumber != null) obj.expectedNumber = Number(a.expectedNumber);
      if (a.expectedNumberMax !== '' && a.expectedNumberMax != null) obj.expectedNumberMax = Number(a.expectedNumberMax);
      if (c.unit) obj.expectedUnit = c.unit;
    }
    if (c.criterionType === 'TEXT' && a.expectedText) obj.expectedText = a.expectedText;
    if ((c.criterionType === 'SINGLE_CHOICE' || c.criterionType === 'MULTI_CHOICE') && a.expectedChoices) {
      obj.expectedChoices = String(a.expectedChoices).split(',').map(x => x.trim()).filter(Boolean);
    }
    return Object.keys(obj).length ? JSON.stringify(obj) : null;
  }

  async function saveDraft(thenSubmit) {
    const btn = content.querySelector('[data-save-draft]');
    const submitBtn = content.querySelector('[data-submit-from-editor]');
    if (busy) return;
    readDraftFromDom();
    const metaForm = content.querySelector('#draft-meta');
    if (metaForm && !metaForm.reportValidity()) return;
    const invalid = content.querySelector('#draft-sections input:invalid, #draft-sections select:invalid, #draft-sections textarea:invalid');
    if (invalid) { invalid.reportValidity(); return; }
    busy = true;
    btn.disabled = true;
    if (submitBtn) submitBtn.disabled = true;
    setStatus('Salvando rascunho…');
    try {
      const payload = {
        changeReason: currentVersion.changeReason,
        validFrom: currentVersion.validFrom || null,
        validUntil: currentVersion.validUntil || null,
        expectedRowVersion: currentVersion.rowVersion,
        sections: currentVersion.sections.map((s, si) => ({
          id: s.id,
          stableKey: s.stableKey,
          name: s.name,
          description: s.description || null,
          sortOrder: si + 1,
          criteria: s.criteria.map((c, ci) => ({
            id: c.id,
            stableKey: c.stableKey,
            name: c.name,
            guidance: c.guidance || null,
            criterionType: c.criterionType,
            required: !!c.required,
            critical: !!c.critical,
            allowNotApplicable: !!c.allowNotApplicable,
            requireNaJustification: !!c.requireNaJustification,
            requireReview: !!c.requireReview,
            unit: c.unit || null,
            weight: c.weight,
            sortOrder: ci + 1,
            approvalConditionJson: buildApprovalJson(c),
            options: (c.criterionType === 'SINGLE_CHOICE' || c.criterionType === 'MULTI_CHOICE') ? c.options : null
          }))
        }))
      };
      await request(`/api/inspections/versions/${currentVersion.id}`, { method: 'PUT', body: JSON.stringify(payload) });
      currentVersion = await request(`/api/inspections/versions/${currentVersion.id}`);
      content.querySelector('[data-draft-saved]').textContent = `Salvo em ${fmt(new Date().toISOString())}`;
      if (thenSubmit) {
        if (!await askConfirm('Enviar este rascunho para revisão? Após o envio a edição fica bloqueada até retorno ou publicação.')) {
          finishLoading(); btn.disabled = false; if (submitBtn) submitBtn.disabled = false; return;
        }
        await request(`/api/inspections/versions/${currentVersion.id}/submit-review`, {
          method: 'POST',
          body: JSON.stringify({ expectedRowVersion: currentVersion.rowVersion })
        });
        currentVersion = null;
        await openModel(currentModel.id);
        finishLoading('Enviado para revisão.');
        return;
      }
      renderDraftEditor();
      finishLoading('Rascunho salvo.');
    } catch (e) {
      finishLoading(e.message);
      notifyError(e.message);
    } finally {
      busy = false;
      btn && (btn.disabled = false);
      submitBtn && (submitBtn.disabled = false);
    }
  }

  async function submitReview(versionId, rowVersion) {
    if (!await askConfirm('Enviar versão para revisão?')) return;
    try {
      await request(`/api/inspections/versions/${versionId}/submit-review`, {
        method: 'POST', body: JSON.stringify({ expectedRowVersion: rowVersion })
      });
      await openModel(currentModel.id);
    } catch (e) { finishLoading(e.message); notifyError(e.message); }
  }

  function publishVersion(versionId, rowVersion) {
    document.querySelector('#insp-form-title').textContent = 'Publicar versão';
    fields.innerHTML = `
      <label>Vigência início <input name="validFrom" type="date" required /></label>
      <label>Vigência fim <input name="validUntil" type="date" /></label>
      <label class="wide">Motivo da publicação <input name="changeReason" maxlength="500" /></label>
      <input type="hidden" name="expectedRowVersion" value="${esc(rowVersion)}" />`;
    form.dataset.mode = 'publish';
    form.dataset.id = versionId;
    dialog.showModal();
  }

  function inactivateEntity(kind, id) {
    document.querySelector('#insp-form-title').textContent = kind === 'model' ? 'Inativar modelo' : 'Inativar versão';
    fields.innerHTML = `<label class="wide">Motivo <textarea name="reason" required maxlength="500"></textarea></label>`;
    form.dataset.mode = kind === 'model' ? 'inactivate-model' : 'inactivate-version';
    form.dataset.id = id;
    dialog.showModal();
  }

  // ——— Runs ———
  async function loadRuns() {
    const qs = new URLSearchParams({ page: '1', pageSize: '50' });
    if (processSel.value) qs.set('processCode', processSel.value);
    if (statusSel.value) qs.set('status', statusSel.value);
    const data = await request(`/api/inspections/runs?${qs}`);
    runs = Array.isArray(data) ? data : (data?.items || []);
    const filtered = runs.filter(qMatch);
    content.innerHTML = filtered.length
      ? `<div class="insp-list">${filtered.map(r => `
          <article class="data-card actionable" tabindex="0" role="button" data-run="${esc(r.id)}">
            <div><strong>${esc(r.number)} · ${esc(r.modelName)} v${esc(r.modelVersionNumber)}</strong>
              <small>${esc(processLabel(r.processCode))} · ${esc(r.inspectorName)} · início ${fmt(r.startedAt)}${r.overallResult ? ` · ${esc(r.overallResult)}` : ''}</small></div>
            ${pill(r.status)}
          </article>`).join('')}</div>`
      : `<div class="empty-state"><h2>Nenhuma inspeção</h2><p>Inicie uma inspeção a partir do contexto do processo. Sem modelo aplicável, a pendência permanece explícita.</p></div>`;
    content.querySelectorAll('[data-run]').forEach(el => {
      const open = async () => {
        currentRun = await request(`/api/inspections/runs/${el.dataset.run}`);
        lastCompleteResult = null;
        const st = String(currentRun.status || '').toUpperCase();
        setView(st === 'COMPLETED' || st === 'CANCELLED' ? 'result' : 'execution');
      };
      el.onclick = open;
      el.onkeydown = e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); open(); } };
    });
  }

  function openStartInspection() {
    document.querySelector('#insp-form-title').textContent = 'Iniciar inspeção';
    const today = new Date().toISOString().slice(0, 10);
    fields.innerHTML = `
      <label>Processo <select name="processCode" required>${PROCESS_OPTS.map(([v, l]) => `<option value="${v}">${l}</option>`).join('')}</select></label>
      <label>Data de referência <input name="referenceDate" type="date" required value="${today}" /></label>
      <label>Unidade <select name="unitId" data-resource="units"><option value="">Opcional…</option></select></label>
      <label>Categoria <input name="productCategory" maxlength="80" /></label>
      <label>Produto <select name="productId" data-resource="products"><option value="">Opcional…</option></select></label>
      <label>Lote <select name="lotId" data-resource="lots"><option value="">Opcional…</option></select></label>
      <label>Inspetor <select name="inspectorId" data-resource="users" required><option value="">Selecione…</option></select></label>
      <label>Tipo de origem <input name="originType" maxlength="40" placeholder="MANUAL" value="MANUAL" /></label>
      <div class="wide" data-resolve-result></div>`;
    form.dataset.mode = 'start-resolve';
    form.dataset.id = '';
    dialog.showModal();
    Promise.all([...fields.querySelectorAll('select[data-resource]')].map(s => fillLookup(s, s.dataset.resource))).then(() => {
      const me = session()?.userId;
      if (me) fields.querySelector('[name=inspectorId]').value = me;
    });
  }

  async function resolveAndStart(body) {
    const box = fields.querySelector('[data-resolve-result]');
    box.innerHTML = '<p role="status">Resolvendo modelo aplicável…</p>';
    const ctx = {
      processCode: body.processCode,
      unitId: body.unitId || null,
      productCategory: body.productCategory || null,
      productId: body.productId || null,
      referenceDate: body.referenceDate
    };
    const resolution = await request('/api/inspections/resolve', { method: 'POST', body: JSON.stringify(ctx) });
    const candidates = resolution.candidates || [];
    if (!candidates.length) {
      box.innerHTML = `<div class="notice danger" role="alert"><strong>Pendência:</strong> nenhum modelo publicado aplicável para este contexto. A inspeção não pode iniciar e não há aprovação automática.</div><p>${esc(resolution.ruleExplanation || '')}</p>`;
      return null;
    }
    if (resolution.ambiguous) {
      const allowManual = candidates.some(c => c.allowManualSelection) || resolution.allowManualSelection;
      // ModelCandidate may not include allowManualSelection — check models list / recommended path
      if (!allowManual) {
        // Fetch model details for candidates to see allowManualSelection
        let anyManual = false;
        for (const c of candidates) {
          try {
            const m = await request(`/api/inspections/models/${c.modelId}`);
            if (m.allowManualSelection) { anyManual = true; c._allow = true; }
          } catch { /* ignore */ }
        }
        if (!anyManual) {
          box.innerHTML = `<div class="notice danger" role="alert"><strong>Ambiguidade bloqueada:</strong> ${esc(resolution.ruleExplanation || 'Mais de um modelo empatou.')} Nenhum candidato permite seleção manual. Ajuste precedência/especificidade ou habilite seleção manual no modelo.</div>
            <ul>${candidates.map(c => `<li>${esc(c.code)} · ${esc(c.name)} (prec. ${esc(c.precedence)}, esp. ${esc(c.specificityScore)})</li>`).join('')}</ul>`;
          return null;
        }
        box.innerHTML = `<div class="notice warn"><strong>Escolha o modelo</strong> (seleção manual permitida):</div>
          <label class="wide">Modelo <select name="chosenVersionId" required>
            ${candidates.filter(c => c._allow).map(c => `<option value="${esc(c.versionId)}">${esc(c.code)} · ${esc(c.name)} v${esc(c.versionNumber)}</option>`).join('')}
          </select></label>`;
        form.dataset.mode = 'start-confirm';
        form.dataset.selectionMode = 'MANUAL';
        form.dataset.startBody = JSON.stringify(body);
        return null;
      }
      box.innerHTML = `<div class="notice warn"><strong>Escolha o modelo</strong>:</div>
        <label class="wide">Modelo <select name="chosenVersionId" required>
          ${candidates.map(c => `<option value="${esc(c.versionId)}">${esc(c.code)} · ${esc(c.name)} v${esc(c.versionNumber)}</option>`).join('')}
        </select></label>`;
      form.dataset.mode = 'start-confirm';
      form.dataset.selectionMode = 'FORCED_CHOICE';
      form.dataset.startBody = JSON.stringify(body);
      return null;
    }
    const versionId = resolution.recommendedVersionId || candidates[0].versionId;
    return startRun(body, versionId, 'AUTOMATIC');
  }

  async function startRun(body, modelVersionId, selectionMode) {
    const payload = {
      modelVersionId,
      processCode: body.processCode,
      originType: body.originType || 'MANUAL',
      originId: body.originId || null,
      productId: body.productId || null,
      lotId: body.lotId || null,
      unitId: body.unitId || null,
      inspectorId: body.inspectorId,
      selectionMode,
      idempotencyKey: uid()
    };
    const res = await request('/api/inspections/runs', { method: 'POST', body: JSON.stringify(payload) });
    const id = res?.id || res;
    currentRun = await request(`/api/inspections/runs/${id}`);
    dialog.close();
    setView('execution');
  }

  // ——— Execution ———
  function renderExecution() {
    if (!currentRun) {
      content.innerHTML = `<div class="empty-state"><h2>Nenhuma execução selecionada</h2><p>Abra uma inspeção em andamento na aba Inspeções ou inicie uma nova.</p></div>`;
      return;
    }
    const run = currentRun;
    const st = String(run.status || '').toUpperCase();
    if (st === 'COMPLETED' || st === 'CANCELLED') {
      content.innerHTML = `<div class="notice">Esta inspeção está ${esc(run.status)}. Consulte a aba Resultado.</div>
        <button type="button" class="primary-button" data-to-result>Ver resultado</button>`;
      content.querySelector('[data-to-result]').onclick = () => setView('result');
      return;
    }
    const progress = run.progress || { totalCriteria: 0, answeredCriteria: 0, requiredPending: 0, percentComplete: 0 };
    const sections = run.sections || [];
    const isMobile = window.matchMedia('(max-width:750px)').matches;
    if (mobileSection >= sections.length) mobileSection = 0;
    content.innerHTML = `
      <div class="insp-panel" id="exec-view">
        <button type="button" class="ghost-button" data-back-runs>← Inspeções</button>
        <h2>${esc(run.number)} · ${esc(run.modelName)} v${esc(run.modelVersionNumber)}</h2>
        <div class="insp-summary">
          <article class="metric-chip"><small>Origem</small><strong>${esc(run.originType || 'MANUAL')}${run.originId ? ` · ${esc(run.originId)}` : ''}</strong></article>
          <article class="metric-chip"><small>Inspetor</small><strong>${esc(run.inspectorName)}</strong></article>
          <article class="metric-chip"><small>Início</small><strong>${fmt(run.startedAt)}</strong></article>
          <article class="metric-chip"><small>Último salvamento</small><strong data-last-saved>${fmt(run.lastSavedAt)}</strong></article>
          <article class="metric-chip"><small>Unidade / produto</small><strong>${esc(run.unitName || '—')} / ${esc(run.productName || '—')}</strong></article>
          <article class="metric-chip"><small>Modo de seleção</small><strong>${esc(run.selectionMode)}</strong></article>
        </div>
        <p><strong>Preenchimento:</strong> ${esc(progress.answeredCriteria)}/${esc(progress.totalCriteria)} critérios
          · <strong>Obrigatórios pendentes:</strong> ${esc(progress.requiredPending)}
          · <strong>Críticos não conformes:</strong> ${esc(progress.criticalNonConforming ?? 0)}</p>
        <div class="progress-bar" aria-hidden="true"><span style="width:${esc(progress.percentComplete || 0)}%"></span></div>
        <p class="notice">O progresso acima mede preenchimento. A aprovação de cada critério e o resultado geral só são definidos na conclusão.</p>
        <div class="section-nav mobile-only">
          <button type="button" data-prev-sec ${mobileSection <= 0 ? 'disabled' : ''}>Anterior</button>
          <span>Seção ${Math.min(mobileSection + 1, sections.length)} de ${sections.length}</span>
          <button type="button" data-next-sec ${mobileSection >= sections.length - 1 ? 'disabled' : ''}>Próxima</button>
        </div>
        <form id="exec-form">${sections.map((s, si) => `
          <section class="insp-section" data-section-index="${si}" data-mobile-hidden="${isMobile && si !== mobileSection}">
            <header><div><h3>${esc(s.name)}</h3><p>${esc(s.description || '')}</p></div></header>
            ${(s.criteria || []).map(c => criterionAnswerHtml(c)).join('')}
          </section>`).join('')}
        </form>
        <div class="insp-actions">
          <button type="button" class="primary-button" data-save-answers>Salvar rascunho</button>
          <button type="button" data-complete-run>Concluir inspeção</button>
          <button type="button" data-cancel-run>Cancelar</button>
        </div>
        <p class="saved-at" data-saved-msg></p>
      </div>`;
    bindExecution();
  }

  function criterionAnswerHtml(c) {
    const a = c.answer || {};
    const na = !!a.isNotApplicable;
    let input = '';
    switch (c.criterionType) {
      case 'PASS_FAIL':
        input = `<label>Resultado <select name="pf-${c.id}" ${na ? 'disabled' : ''}><option value="">Selecione…</option>
          <option value="true" ${a.passFailValue === true ? 'selected' : ''}>PASS</option>
          <option value="false" ${a.passFailValue === false ? 'selected' : ''}>FAIL</option></select></label>`;
        break;
      case 'SINGLE_CHOICE':
        input = `<label>Opção <select name="sc-${c.id}" ${na ? 'disabled' : ''}><option value="">Selecione…</option>
          ${(c.options || []).map(o => `<option value="${esc(o.value)}" ${(a.choiceValues || [])[0] === o.value ? 'selected' : ''}>${esc(o.label)}</option>`).join('')}</select></label>`;
        break;
      case 'MULTI_CHOICE':
        input = `<fieldset ${na ? 'disabled' : ''}><legend>Opções</legend>${(c.options || []).map(o => `
          <label><input type="checkbox" name="mc-${c.id}" value="${esc(o.value)}" ${(a.choiceValues || []).includes(o.value) ? 'checked' : ''} /> ${esc(o.label)}</label>`).join('')}</fieldset>`;
        break;
      case 'TEXT':
        input = `<label class="wide">Texto <textarea name="tx-${c.id}" maxlength="4000" ${na ? 'disabled' : ''}>${esc(a.textValue || '')}</textarea></label>`;
        break;
      case 'NUMBER':
        input = `<label>Valor <input name="nm-${c.id}" type="number" step="any" value="${esc(a.numberValue ?? '')}" ${na ? 'disabled' : ''} /></label>
          <label>Unidade <input name="nu-${c.id}" value="${esc(a.numberUnit || c.unit || '')}" maxlength="30" ${na ? 'disabled' : ''} /></label>`;
        break;
      case 'DATE':
        input = `<label>Data <input name="dt-${c.id}" type="date" value="${esc(a.dateValue || '')}" ${na ? 'disabled' : ''} /></label>`;
        break;
      case 'DOCUMENT_EVIDENCE':
        input = `<label class="wide">Evidência documental <select name="doc-${c.id}" data-resource="documents" ${na ? 'disabled' : ''}><option value="">Selecione…</option></select></label>`;
        break;
      default:
        input = `<p class="notice warn">Tipo ${esc(c.criterionType)} não suportado nesta tela.</p>`;
    }
    return `<article class="criterion-card ${c.critical ? 'critical' : ''}" data-criterion-id="${esc(c.id)}" data-type="${esc(c.criterionType)}">
      <strong>${esc(c.name)}</strong>
      <div class="criterion-meta">
        <span>${esc(typeLabel(c.criterionType))}</span>
        ${c.required ? '<span>Obrigatório</span>' : ''}
        ${c.critical ? '<span>Crítico</span>' : ''}
        ${c.weight != null ? `<span>Peso ${esc(c.weight)}</span>` : ''}
        ${a.outcome ? `<span>Avaliação: ${esc(a.outcome)}</span>` : '<span>Avaliação: pendente até concluir</span>'}
      </div>
      ${c.guidance ? `<p><small>${esc(c.guidance)}</small></p>` : ''}
      <div class="form-grid">
        ${c.allowNotApplicable ? `<label><input type="checkbox" data-na ${na ? 'checked' : ''} /> Não aplicável (N/A)</label>
          <label class="wide">Justificativa N/A ${c.requireNaJustification ? '(obrigatória)' : ''} <textarea data-na-just maxlength="1000" ${na ? '' : 'disabled'}>${esc(a.notApplicableJustification || '')}</textarea></label>` : ''}
        ${input}
        <label class="wide">Notas <textarea data-notes maxlength="1000">${esc(a.notes || '')}</textarea></label>
      </div>
    </article>`;
  }

  function bindExecution() {
    const root = content.querySelector('#exec-view');
    root.querySelector('[data-back-runs]').onclick = () => { currentRun = null; setView('runs'); };
    root.querySelector('[data-prev-sec]')?.addEventListener('click', () => { mobileSection = Math.max(0, mobileSection - 1); renderExecution(); });
    root.querySelector('[data-next-sec]')?.addEventListener('click', () => { mobileSection = Math.min((currentRun.sections || []).length - 1, mobileSection + 1); renderExecution(); });
    root.querySelectorAll('[data-na]').forEach(cb => cb.onchange = () => {
      const card = cb.closest('[data-criterion-id]');
      const just = card.querySelector('[data-na-just]');
      if (just) just.disabled = !cb.checked;
      card.querySelectorAll('input:not([data-na]):not([data-notes]), select, textarea:not([data-na-just]):not([data-notes])').forEach(el => {
        if (el.closest('fieldset')) el.closest('fieldset').disabled = cb.checked;
        else el.disabled = cb.checked;
      });
    });
    root.querySelectorAll('select[data-resource]').forEach(async s => {
      await fillLookup(s, s.dataset.resource);
      const cid = s.closest('[data-criterion-id]')?.dataset.criterionId;
      const crit = (currentRun.sections || []).flatMap(x => x.criteria || []).find(c => c.id === cid);
      if (crit?.answer?.documentEvidenceId) s.value = crit.answer.documentEvidenceId;
    });
    root.querySelector('[data-save-answers]').onclick = () => saveAnswers();
    root.querySelector('[data-complete-run]').onclick = () => completeRun();
    root.querySelector('[data-cancel-run]').onclick = () => cancelRun();
  }

  function collectAnswers() {
    const answers = [];
    content.querySelectorAll('[data-criterion-id]').forEach(card => {
      const id = card.dataset.criterionId;
      const type = card.dataset.type;
      const isNa = !!card.querySelector('[data-na]')?.checked;
      const item = {
        criterionId: id,
        isNotApplicable: isNa,
        notApplicableJustification: card.querySelector('[data-na-just]')?.value || null,
        passFailValue: null,
        textValue: null,
        numberValue: null,
        numberUnit: null,
        dateValue: null,
        choiceValues: null,
        documentEvidenceId: null,
        notes: card.querySelector('[data-notes]')?.value || null
      };
      if (!isNa) {
        if (type === 'PASS_FAIL') {
          const v = card.querySelector(`[name="pf-${id}"]`)?.value;
          item.passFailValue = v === '' ? null : v === 'true';
        } else if (type === 'SINGLE_CHOICE') {
          const v = card.querySelector(`[name="sc-${id}"]`)?.value;
          item.choiceValues = v ? [v] : null;
        } else if (type === 'MULTI_CHOICE') {
          item.choiceValues = [...card.querySelectorAll(`[name="mc-${id}"]:checked`)].map(x => x.value);
        } else if (type === 'TEXT') {
          item.textValue = card.querySelector(`[name="tx-${id}"]`)?.value || null;
        } else if (type === 'NUMBER') {
          const n = card.querySelector(`[name="nm-${id}"]`)?.value;
          item.numberValue = n === '' || n == null ? null : Number(n);
          item.numberUnit = card.querySelector(`[name="nu-${id}"]`)?.value || null;
        } else if (type === 'DATE') {
          item.dateValue = card.querySelector(`[name="dt-${id}"]`)?.value || null;
        } else if (type === 'DOCUMENT_EVIDENCE') {
          const v = card.querySelector(`[name="doc-${id}"]`)?.value;
          item.documentEvidenceId = v || null;
        }
      }
      answers.push(item);
    });
    return answers;
  }

  async function saveAnswers() {
    const btn = content.querySelector('[data-save-answers]');
    if (!btn || btn.disabled || busy) return;
    const formEl = content.querySelector('#exec-form');
    if (formEl && !formEl.reportValidity()) return;
    busy = true;
    btn.disabled = true;
    const msg = content.querySelector('[data-saved-msg]');
    if (msg) msg.textContent = '';
    setStatus('Salvando respostas…');
    try {
      const result = await request(`/api/inspections/runs/${currentRun.id}/answers`, {
        method: 'PUT',
        body: JSON.stringify({ answers: collectAnswers(), expectedRowVersion: currentRun.rowVersion })
      });
      currentRun.rowVersion = result.rowVersion;
      currentRun.lastSavedAt = result.lastSavedAt;
      const savedLabel = content.querySelector('[data-last-saved]');
      if (savedLabel) savedLabel.textContent = fmt(result.lastSavedAt);
      if (msg) msg.textContent = `Salvo em ${fmt(result.lastSavedAt)}`;
      finishLoading();
    } catch (e) {
      if (e.status === 409) {
        notifyError('Conflito de versão (409): outro salvamento ocorreu. A inspeção será recarregada.');
        try {
          currentRun = await request(`/api/inspections/runs/${currentRun.id}`);
          renderExecution();
        } catch (reloadErr) { finishLoading(reloadErr.message); }
        finishLoading('Conflito resolvido com recarga.');
      } else {
        finishLoading(e.message);
        if (msg) msg.textContent = e.message;
      }
    } finally {
      busy = false;
      if (btn) btn.disabled = false;
    }
  }

  async function completeRun() {
    if (!await askConfirm('Concluir a inspeção? O resultado geral e os critérios determinantes serão calculados e preservados.')) return;
    const btn = content.querySelector('[data-complete-run]');
    if (btn) btn.disabled = true;
    busy = true;
    setStatus('Concluindo…');
    try {
      await saveAnswersSilent();
      lastCompleteResult = await request(`/api/inspections/runs/${currentRun.id}/complete`, {
        method: 'POST',
        body: JSON.stringify({ expectedRowVersion: currentRun.rowVersion, idempotencyKey: uid() })
      });
      currentRun = await request(`/api/inspections/runs/${currentRun.id}`);
      setView('result');
    } catch (e) {
      finishLoading(e.message);
      notifyError(e.message);
      if (btn) btn.disabled = false;
      busy = false;
    }
  }

  async function saveAnswersSilent() {
    const result = await request(`/api/inspections/runs/${currentRun.id}/answers`, {
      method: 'PUT',
      body: JSON.stringify({ answers: collectAnswers(), expectedRowVersion: currentRun.rowVersion })
    });
    currentRun.rowVersion = result.rowVersion;
    currentRun.lastSavedAt = result.lastSavedAt;
  }

  function cancelRun() {
    document.querySelector('#insp-form-title').textContent = 'Cancelar inspeção';
    fields.innerHTML = `<label class="wide">Motivo <textarea name="reason" required maxlength="500"></textarea></label>`;
    form.dataset.mode = 'cancel-run';
    form.dataset.id = currentRun.id;
    dialog.showModal();
  }

  // ——— Result ———
  function renderResult() {
    if (!currentRun) {
      content.innerHTML = `<div class="empty-state"><h2>Nenhum resultado</h2><p>Conclua uma inspeção ou abra uma já finalizada.</p></div>`;
      return;
    }
    const run = currentRun;
    const keys = run.determiningStableKeys || lastCompleteResult?.determiningStableKeys || [];
    const overall = run.overallResult || lastCompleteResult?.overallResult;
    const score = run.weightedScorePercent ?? lastCompleteResult?.weightedScorePercent;
    const effects = lastCompleteResult?.effects;
    const determining = (run.sections || []).flatMap(s => s.criteria || []).filter(c => keys.includes(c.stableKey));
    content.innerHTML = `
      <div class="insp-panel">
        <button type="button" class="ghost-button" data-back-runs>← Inspeções</button>
        <h2>Resultado · ${esc(run.number)}</h2>
        <div class="insp-summary">
          <article class="metric-chip"><small>Status</small><strong>${pill(run.status)}</strong></article>
          <article class="metric-chip"><small>Resultado geral</small><strong>${pill(overall || '—')}</strong></article>
          <article class="metric-chip"><small>Score ponderado</small><strong>${score == null ? '—' : esc(score) + '%'}</strong></article>
          <article class="metric-chip"><small>Concluída em</small><strong>${fmt(run.completedAt)}</strong></article>
        </div>
        <section class="insp-section">
          <h3>Explicação do resultado</h3>
          <p>O resultado geral não é apenas uma cor: ele deriva dos critérios determinantes (especialmente críticos) e, quando aplicável, do score ponderado excluindo N/A. Falha crítica não é compensada por outros critérios.</p>
          ${keys.length ? `<ul>${keys.map(k => {
            const c = determining.find(x => x.stableKey === k);
            return `<li><strong>${esc(k)}</strong>${c ? ` · ${esc(c.name)} · ${esc(c.answer?.outcome || '')}` : ''}</li>`;
          }).join('')}</ul>` : '<p class="notice">Nenhum critério determinante informado pelo servidor.</p>'}
        </section>
        ${effects ? `<section class="insp-section"><h3>Efeitos sugeridos</h3>
          <p>${effects.nonConformitySuggested ? 'Sugestão de não conformidade. ' : ''}${effects.lotHoldSuggested ? 'Sugestão de retenção de lote. ' : ''}</p>
          <ul>${(effects.messages || []).map(m => `<li>${esc(m)}</li>`).join('')}</ul>
        </section>` : ''}
        <section class="insp-section"><h3>Respostas</h3>
          ${(run.sections || []).map(s => `<article class="insp-section"><h4>${esc(s.name)}</h4>
            ${(s.criteria || []).map(c => `<p><strong>${esc(c.name)}</strong> · ${esc(c.answer?.outcome || (c.answer?.isNotApplicable ? 'N/A' : '—'))}
              <br/><small>${summarizeAnswer(c)}</small></p>`).join('')}
          </article>`).join('')}
        </section>
        <div class="insp-actions">
          ${String(run.status).toUpperCase() === 'COMPLETED' ? `<button type="button" class="primary-button" data-reinspect>Reinspecionar</button>` : ''}
        </div>
      </div>`;
    content.querySelector('[data-back-runs]').onclick = () => { currentRun = null; lastCompleteResult = null; setView('runs'); };
    content.querySelector('[data-reinspect]')?.addEventListener('click', () => {
      document.querySelector('#insp-form-title').textContent = 'Reinspeção';
      fields.innerHTML = `
        <label class="wide">Motivo <textarea name="reason" required maxlength="500"></textarea></label>
        <label>Usar última versão publicada <select name="useLatestPublishedVersion"><option value="true">Sim</option><option value="false">Não (mesma versão)</option></select></label>`;
      form.dataset.mode = 'reinspect';
      form.dataset.id = run.id;
      dialog.showModal();
    });
  }

  function summarizeAnswer(c) {
    const a = c.answer;
    if (!a) return 'Sem resposta';
    if (a.isNotApplicable) return `N/A: ${a.notApplicableJustification || 'sem justificativa'}`;
    if (a.passFailValue != null) return a.passFailValue ? 'PASS' : 'FAIL';
    if (a.textValue) return a.textValue;
    if (a.numberValue != null) return `${a.numberValue} ${a.numberUnit || c.unit || ''}`;
    if (a.dateValue) return a.dateValue;
    if (a.choiceValues?.length) return a.choiceValues.join(', ');
    if (a.documentEvidenceId) return `Documento ${a.documentEvidenceId}`;
    return '—';
  }

  // ——— Schedules ———
  async function loadSchedules() {
    schedules = await request('/api/inspections/schedules') || [];
    if (!Array.isArray(schedules)) schedules = schedules.items || [];
    const filtered = schedules.filter(s => {
      if (statusSel.value && s.status !== statusSel.value) return false;
      if (processSel.value && s.processCode !== processSel.value) return false;
      return qMatch(s);
    });
    content.innerHTML = filtered.length
      ? `<div class="insp-list">${filtered.map(s => `
          <article class="data-card">
            <div><strong>${esc(s.name)}</strong>
              <small>${esc(processLabel(s.processCode))} · ${esc(s.cadence)} · modelo ${esc(s.modelName || s.modelId)} · próximo ${esc(s.nextDueOn || '—')}</small></div>
            <div class="insp-actions">
              ${pill(s.status)}
              ${String(s.status).toUpperCase() === 'ACTIVE' ? `<button type="button" data-edit-sched="${esc(s.id)}">Editar</button>
              <button type="button" data-inactivate-sched="${esc(s.id)}">Inativar</button>` : ''}
            </div>
          </article>`).join('')}</div>`
      : `<div class="empty-state"><h2>Nenhuma programação</h2><p>Crie programações Única, Periódica ou por Evento.</p></div>`;
    content.querySelectorAll('[data-edit-sched]').forEach(b => b.onclick = () => openScheduleForm(schedules.find(x => x.id === b.dataset.editSched)));
    content.querySelectorAll('[data-inactivate-sched]').forEach(b => b.onclick = () => {
      document.querySelector('#insp-form-title').textContent = 'Inativar programação';
      fields.innerHTML = `<label class="wide">Motivo <textarea name="reason" required maxlength="500"></textarea></label>`;
      form.dataset.mode = 'inactivate-schedule';
      form.dataset.id = b.dataset.inactivateSched;
      dialog.showModal();
    });
  }

  async function openScheduleForm(existing) {
    document.querySelector('#insp-form-title').textContent = existing ? 'Editar programação' : 'Nova programação';
    if (!models.length) {
      try { models = await request('/api/inspections/models?status=ACTIVE') || []; if (!Array.isArray(models)) models = models.items || []; } catch { models = []; }
    }
    fields.innerHTML = `
      <label class="wide">Nome <input name="name" required maxlength="180" value="${esc(existing?.name || '')}" /></label>
      <label>Processo <select name="processCode" required>${PROCESS_OPTS.map(([v, l]) => `<option value="${v}" ${existing?.processCode === v ? 'selected' : ''}>${l}</option>`).join('')}</select></label>
      <label>Modelo <select name="modelId" required><option value="">Selecione…</option>${models.map(m => `<option value="${esc(m.id)}" ${existing?.modelId === m.id ? 'selected' : ''}>${esc(m.code)} · ${esc(m.name)}</option>`).join('')}</select></label>
      <label>Cadência <select name="cadence" required>
        <option value="ONCE" ${existing?.cadence === 'ONCE' ? 'selected' : ''}>Única (ONCE)</option>
        <option value="PERIODIC" ${existing?.cadence === 'PERIODIC' ? 'selected' : ''}>Periódica</option>
        <option value="EVENT" ${existing?.cadence === 'EVENT' ? 'selected' : ''}>Evento</option>
      </select></label>
      <label>Intervalo (dias) <small class="hint">Obrigatório para PERIODIC</small><input name="intervalDays" type="number" min="1" value="${esc(existing?.intervalDays ?? '')}" /></label>
      <label>Código do evento <input name="eventCode" maxlength="60" value="${esc(existing?.eventCode || '')}" /></label>
      <label>Início <input name="startsOn" type="date" value="${esc(existing?.startsOn || '')}" /></label>
      <label>Fim <input name="endsOn" type="date" value="${esc(existing?.endsOn || '')}" /></label>
      <label>Unidade <select name="unitId" data-resource="units"><option value="">Opcional…</option></select></label>
      <label>Categoria <input name="productCategory" maxlength="80" value="${esc(existing?.productCategory || '')}" /></label>
      <label>Produto <select name="productId" data-resource="products"><option value="">Opcional…</option></select></label>
      <label>Responsável <select name="responsibleId" data-resource="users"><option value="">Opcional…</option></select></label>
      <label>Permitir catch-up <select name="allowCatchUp"><option value="false">Não</option><option value="true" ${existing?.allowCatchUp ? 'selected' : ''}>Sim</option></select></label>
      <label class="wide">Notas <textarea name="notes" maxlength="2000">${esc(existing?.notes || '')}</textarea></label>
      <input type="hidden" name="expectedRowVersion" value="${esc(existing?.rowVersion ?? '')}" />`;
    form.dataset.mode = existing ? 'update-schedule' : 'create-schedule';
    form.dataset.id = existing?.id || '';
    dialog.showModal();
    await Promise.all([...fields.querySelectorAll('select[data-resource]')].map(s => fillLookup(s, s.dataset.resource)));
    if (existing?.unitId) fields.querySelector('[name=unitId]').value = existing.unitId;
    if (existing?.productId) fields.querySelector('[name=productId]').value = existing.productId;
    if (existing?.responsibleId) fields.querySelector('[name=responsibleId]').value = existing.responsibleId;
  }

  // ——— Dialog submit ———
  form.addEventListener('submit', async e => {
    e.preventDefault();
    if (!form.reportValidity()) return;
    if (busy) return;
    const mode = form.dataset.mode;
    const id = form.dataset.id;
    const msg = form.querySelector('.form-message');
    msg.textContent = '';
    const raw = Object.fromEntries(new FormData(form));
    const emptyToNull = v => (v === '' || v == null ? null : v);
    const btn = form.querySelector('[type=submit]');
    btn.disabled = true;
    busy = true;
    try {
      if (mode === 'create-model') {
        if (!await askConfirm('Criar este modelo?')) { busy = false; btn.disabled = false; return; }
        const body = {
          code: raw.code, name: raw.name, description: emptyToNull(raw.description), processCode: raw.processCode,
          precedence: Number(raw.precedence), allowManualSelection: raw.allowManualSelection === 'true',
          unitId: emptyToNull(raw.unitId), productCategory: emptyToNull(raw.productCategory), productId: emptyToNull(raw.productId),
          instructions: emptyToNull(raw.instructions), reviewResponsibleId: emptyToNull(raw.reviewResponsibleId)
        };
        const res = await request('/api/inspections/models', { method: 'POST', body: JSON.stringify(body) });
        dialog.close();
        await openModel(res?.id || res);
      } else if (mode === 'update-model') {
        const body = {
          name: raw.name, description: emptyToNull(raw.description), precedence: Number(raw.precedence),
          allowManualSelection: raw.allowManualSelection === 'true', unitId: emptyToNull(raw.unitId),
          productCategory: emptyToNull(raw.productCategory), productId: emptyToNull(raw.productId),
          instructions: emptyToNull(raw.instructions), reviewResponsibleId: emptyToNull(raw.reviewResponsibleId),
          expectedRowVersion: Number(raw.expectedRowVersion)
        };
        await request(`/api/inspections/models/${id}`, { method: 'PUT', body: JSON.stringify(body) });
        dialog.close();
        await openModel(id);
      } else if (mode === 'publish') {
        if (!await askConfirm('Publicar esta versão? O conteúdo publicado torna-se imutável.')) { busy = false; btn.disabled = false; return; }
        await request(`/api/inspections/versions/${id}/publish`, {
          method: 'POST',
          body: JSON.stringify({
            validFrom: raw.validFrom,
            validUntil: emptyToNull(raw.validUntil),
            expectedRowVersion: Number(raw.expectedRowVersion),
            changeReason: emptyToNull(raw.changeReason)
          })
        });
        dialog.close();
        await openModel(currentModel.id);
      } else if (mode === 'inactivate-model') {
        if (!await askConfirm('Inativar o modelo?')) { busy = false; btn.disabled = false; return; }
        await request(`/api/inspections/models/${id}/inactivate`, { method: 'POST', body: JSON.stringify({ reason: raw.reason }) });
        dialog.close();
        currentModel = null;
        await load();
      } else if (mode === 'inactivate-version') {
        if (!await askConfirm('Inativar a versão?')) { busy = false; btn.disabled = false; return; }
        await request(`/api/inspections/versions/${id}/inactivate`, { method: 'POST', body: JSON.stringify({ reason: raw.reason }) });
        dialog.close();
        await openModel(currentModel.id);
      } else if (mode === 'start-resolve') {
        const body = {
          processCode: raw.processCode, referenceDate: raw.referenceDate,
          unitId: emptyToNull(raw.unitId), productCategory: emptyToNull(raw.productCategory),
          productId: emptyToNull(raw.productId), lotId: emptyToNull(raw.lotId),
          inspectorId: raw.inspectorId, originType: emptyToNull(raw.originType) || 'MANUAL'
        };
        await resolveAndStart(body);
      } else if (mode === 'start-confirm') {
        const body = JSON.parse(form.dataset.startBody || '{}');
        body.inspectorId = raw.inspectorId || body.inspectorId;
        await startRun(body, raw.chosenVersionId, form.dataset.selectionMode || 'MANUAL');
      } else if (mode === 'cancel-run') {
        if (!await askConfirm('Cancelar esta inspeção?')) { busy = false; btn.disabled = false; return; }
        await request(`/api/inspections/runs/${id}/cancel`, {
          method: 'POST',
          body: JSON.stringify({ reason: raw.reason, expectedRowVersion: currentRun.rowVersion })
        });
        dialog.close();
        currentRun = await request(`/api/inspections/runs/${id}`);
        setView('result');
      } else if (mode === 'reinspect') {
        if (!await askConfirm('Criar reinspeção a partir desta execução?')) { busy = false; btn.disabled = false; return; }
        const res = await request(`/api/inspections/runs/${id}/reinspections`, {
          method: 'POST',
          body: JSON.stringify({
            reason: raw.reason,
            useLatestPublishedVersion: raw.useLatestPublishedVersion === 'true',
            idempotencyKey: uid()
          })
        });
        dialog.close();
        currentRun = await request(`/api/inspections/runs/${res?.id || res}`);
        lastCompleteResult = null;
        setView('execution');
      } else if (mode === 'create-schedule' || mode === 'update-schedule') {
        if (raw.cadence === 'PERIODIC' && !raw.intervalDays) {
          msg.textContent = 'Informe o intervalo em dias para cadência periódica.';
          busy = false; btn.disabled = false; return;
        }
        if (raw.cadence === 'EVENT' && !raw.eventCode) {
          msg.textContent = 'Informe o código do evento.';
          busy = false; btn.disabled = false; return;
        }
        const body = {
          name: raw.name, processCode: raw.processCode, modelId: raw.modelId,
          unitId: emptyToNull(raw.unitId), productCategory: emptyToNull(raw.productCategory), productId: emptyToNull(raw.productId),
          cadence: raw.cadence, intervalDays: raw.intervalDays ? Number(raw.intervalDays) : null,
          eventCode: emptyToNull(raw.eventCode), startsOn: emptyToNull(raw.startsOn), endsOn: emptyToNull(raw.endsOn),
          responsibleId: emptyToNull(raw.responsibleId), notes: emptyToNull(raw.notes),
          allowCatchUp: raw.allowCatchUp === 'true',
          expectedRowVersion: raw.expectedRowVersion ? Number(raw.expectedRowVersion) : null
        };
        if (mode === 'create-schedule') await request('/api/inspections/schedules', { method: 'POST', body: JSON.stringify(body) });
        else await request(`/api/inspections/schedules/${id}`, { method: 'PUT', body: JSON.stringify(body) });
        dialog.close();
        await load();
      } else if (mode === 'inactivate-schedule') {
        if (!await askConfirm('Inativar esta programação?')) { busy = false; btn.disabled = false; return; }
        await request(`/api/inspections/schedules/${id}/inactivate`, { method: 'POST', body: JSON.stringify({ reason: raw.reason }) });
        dialog.close();
        await load();
      }
      finishLoading();
    } catch (err) {
      msg.textContent = err.message;
      finishLoading(err.message);
    } finally {
      busy = false;
      btn.disabled = false;
    }
  });

  // ——— Intents ———
  async function loadIntents() {
    const qs = new URLSearchParams();
    if (processSel.value) qs.set('process', processSel.value);
    if (statusSel.value) qs.set('status', statusSel.value);
    let data;
    try {
      data = await request(`/api/inspections/event-intents?${qs}`);
    } catch (err) {
      if (err.status === 401 || err.status === 403) {
        content.innerHTML = `<div class="empty-state"><h2>Acesso restrito</h2><p>Você não possui permissão para visualizar o histórico de gatilhos operacionais de qualidade.</p></div>`;
        return;
      }
      throw err;
    }
    intents = Array.isArray(data) ? data : (data?.items || []);
    const filtered = intents.filter(qMatch);

    const hasPendingModel = filtered.some(it => it.status === 'PENDING_MODEL');
    if (hasPendingModel) {
      window.agro360Feedback?.toast(
        'warning',
        'Modelo de qualidade pendente',
        'Existem eventos operacionais aguardando parametrização de modelo de inspeção.',
        'pending-model-intent'
      );
    }

    const bannerHtml = hasPendingModel ? `
      <div class="banner warning-banner" style="background: rgba(234, 179, 8, 0.15); border: 1px solid #eab308; border-radius: 8px; padding: 1rem; margin-bottom: 1rem;" role="alert">
        <strong style="color: #ca8a04;">Atenção:</strong> Há eventos operacionais com status <em>Sem Modelo (PENDING_MODEL)</em>. Cadastre e publique o modelo de inspeção correspondente para liberar os processos associados.
      </div>
    ` : '';

    content.innerHTML = filtered.length
      ? `${bannerHtml}<div class="insp-list">${filtered.map(it => `
          <article class="data-card" tabindex="0">
            <div>
              <strong>${esc(processLabel(it.processCode))}</strong>
              <small>Origem: ${esc(it.originType)} · Data: ${fmt(it.createdAt)}</small>
              ${it.payloadSummary ? `<p class="hint" style="margin-top:0.25rem;font-size:0.85rem">${esc(it.payloadSummary)}</p>` : ''}
              ${it.notes ? `<p class="notice" style="margin-top:0.5rem">${esc(it.notes)}</p>` : ''}
            </div>
            <div class="insp-actions" style="display:flex;flex-direction:column;align-items:flex-end;gap:0.5rem">
              <span class="status-pill status-${esc(String(it.status || '').toLowerCase())}">${esc(intentStatusLabel(it.status))}</span>
              ${it.inspectionRunId ? `<button type="button" class="ghost-button" data-view-run="${esc(it.inspectionRunId)}">Ver inspeção</button>` : ''}
            </div>
          </article>`).join('')}</div>`
      : `${bannerHtml}<div class="empty-state"><h2>Nenhum gatilho de evento</h2><p>Recebimentos de compra, colheita, devoluções e apontamentos de produção registram intenções automáticas de inspeção.</p></div>`;

    content.querySelectorAll('[data-view-run]').forEach(b => {
      b.onclick = async () => {
        try {
          setStatus('Carregando inspeção…');
          currentRun = await request(`/api/inspections/runs/${b.dataset.viewRun}`);
          lastCompleteResult = null;
          const st = String(currentRun.status || '').toUpperCase();
          setView(st === 'COMPLETED' || st === 'CANCELLED' ? 'result' : 'execution');
        } catch (e) {
          notifyError('Não foi possível carregar a inspeção: ' + e.message);
        } finally {
          finishLoading();
        }
      };
    });
  }

  primaryBtn.addEventListener('click', () => {
    if (view === 'models') { currentModel = null; openModelMetaForm(null); }
    else if (view === 'runs') openStartInspection();
    else if (view === 'schedules') openScheduleForm(null);
    else if (view === 'execution') saveAnswers();
    else if (view === 'result') { currentRun = null; setView('runs'); }
    else if (view === 'intents') loadIntents();
  });

  document.querySelectorAll('.inspections-tabs button').forEach(b => b.onclick = () => {
    if (b.dataset.view === 'models') currentModel = null;
    setView(b.dataset.view);
  });
  document.querySelector('#insp-refresh').onclick = load;
  processSel.onchange = () => { if (view === 'models' || view === 'runs' || view === 'schedules' || view === 'intents') load(); };
  statusSel.onchange = () => { if (view === 'models' || view === 'runs' || view === 'schedules' || view === 'intents') load(); };
  searchInput.oninput = () => {
    if (view === 'models' && !currentModel) loadModels();
    else if (view === 'runs') loadRuns();
    else if (view === 'schedules') loadSchedules();
    else if (view === 'intents') loadIntents();
  };
  document.querySelector('[data-close]').onclick = () => dialog.close();

  syncStatusFilter();
  syncPrimary();
  load();
})();
