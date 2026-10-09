(() => {
  const api = '/api/v1/inventory/stock-control';
  const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const problem = async response => { if (response.status === 401) return 'Sua sessão expirou. Entre novamente.'; if (response.status === 403) return 'Você não possui permissão para esta ação.'; if (response.status === 409) return (await response.json().catch(() => ({}))).detail || 'O registro foi alterado. Recarregue e tente novamente.'; return (await response.json().catch(() => ({}))).detail || 'Não foi possível concluir a operação.'; };
  const request = async (url, options) => { const r = await fetch(url, {headers:{'Content-Type':'application/json'}, ...options}); if (!r.ok) throw new Error(await problem(r)); return r.status === 204 ? null : r.json(); };
  const key = () => crypto.randomUUID();
  let transfer, count;

  document.querySelectorAll('[data-stock-tab]').forEach(button => button.addEventListener('click', () => {
    const name = button.dataset.stockTab;
    document.querySelectorAll('[data-stock-panel]').forEach(p => p.hidden = p.dataset.stockPanel !== name);
    history.replaceState(null, '', `?section=${name}`);
    if (name === 'transfers') loadTransfers();
    if (name === 'counts') loadCounts();
  }));
  document.querySelector(`[data-stock-tab="${new URLSearchParams(location.search).get('section') || 'requests'}"]`)?.click();
  document.querySelectorAll('[data-open]').forEach(b => b.addEventListener('click', () => document.getElementById(b.dataset.open)?.showModal()));
  document.querySelectorAll('#transfer-dialog [data-close],#count-dialog [data-close],#stock-action-dialog [data-close]').forEach(b => b.addEventListener('click', () => b.closest('dialog').close()));

  Promise.all(['farms','warehouses','products','people'].map(async type => {
    try {
      const values = await request(`/api/v1/inventory/material-requests/lookups/${type}`);
      document.querySelectorAll(`[data-ops-lookup="${type}"]`).forEach(select => {
        select.innerHTML = `<option value="">Selecione</option>` + values.map(x => `<option value="${x.id}" data-unit="${esc(x.unit)}">${esc(x.label)}</option>`).join('');
      });
    } catch { /* form displays lookup failure when submitted */ }
  }));
  document.querySelectorAll('[data-ops-lookup="products"]').forEach(s => s.addEventListener('change', () => {
    const unit = s.selectedOptions[0]?.dataset.unit;
    const form = s.closest('form');
    if (form?.elements.unit) form.elements.unit.value = unit || '';
  }));

  document.getElementById('transfer-form')?.addEventListener('submit', async e => {
    e.preventDefault();
    const f = e.currentTarget, m = f.querySelector('.form-message'), d = Object.fromEntries(new FormData(f));
    try {
      if (d.sourceWarehouseId === d.destinationWarehouseId) throw new Error('Origem e destino devem ser diferentes.');
      await request(`${api}/transfers`, {
        method: 'POST',
        body: JSON.stringify({
          farmId: d.farmId,
          sourceWarehouseId: d.sourceWarehouseId,
          destinationWarehouseId: d.destinationWarehouseId,
          responsibleId: d.responsibleId,
          expectedOn: d.expectedOn,
          justification: d.justification,
          items: [{ productId: d.productId, lotNumber: d.lotNumber || null, quantity: Number(d.quantity), unit: d.unit, allowBlocked: false }]
        })
      });
      f.reset();
      f.closest('dialog').close();
      await loadTransfers();
    } catch (err) { m.textContent = err.message; }
  });

  document.getElementById('count-form')?.addEventListener('submit', async e => {
    e.preventDefault();
    const f = e.currentTarget, m = f.querySelector('.form-message'), d = Object.fromEntries(new FormData(f));
    try {
      await request(`${api}/counts`, {
        method: 'POST',
        body: JSON.stringify({
          warehouseId: d.warehouseId,
          responsibleId: d.responsibleId,
          productId: d.productId || null,
          category: d.category || null,
          lotNumber: d.lotNumber || null,
          materialStatus: null,
          blind: !!d.blind
        })
      });
      f.reset();
      f.closest('dialog').close();
      await loadCounts();
    } catch (err) { m.textContent = err.message; }
  });

  document.querySelectorAll('[data-filter]').forEach(f => f.addEventListener('submit', e => {
    e.preventDefault();
    f.dataset.filter === 'transfers' ? loadTransfers() : loadCounts();
  }));

  async function loadTransfers() {
    const root = document.getElementById('transfer-list');
    root.innerHTML = '<p>Carregando transferências…</p>';
    try {
      const s = document.querySelector('[data-filter="transfers"] [name=status]').value;
      const data = await request(`${api}/transfers?status=${encodeURIComponent(s)}`);
      root.innerHTML = data.items.length
        ? `<table><thead><tr><th>Número</th><th>Rota</th><th>Previsão</th><th>Situação</th><th></th></tr></thead><tbody>${data.items.map(x => `<tr><td>#${x.number}</td><td>${esc(x.source)} → ${esc(x.destination)}</td><td>${x.expectedOn}</td><td><span class="status-badge">${esc(x.status)}</span></td><td><button data-transfer="${x.id}">Detalhes</button></td></tr>`).join('')}</tbody></table>`
        : '<p class="empty-state">Nenhuma transferência encontrada.</p>';
      root.querySelectorAll('[data-transfer]').forEach(b => b.onclick = () => showTransfer(b.dataset.transfer));
    } catch (err) { root.innerHTML = `<p role="alert">${esc(err.message)}</p>`; }
  }

  async function showTransfer(id) {
    transfer = await request(`${api}/transfers/${id}`);
    const root = document.getElementById('transfer-detail');
    root.hidden = false;
    root.innerHTML = `<header><h2>Transferência #${transfer.number}</h2><span class="status-badge">${esc(transfer.status)}</span></header><p class="route"><b>${esc(transfer.source)}</b> <span aria-label="para">→</span> <b>${esc(transfer.destination)}</b></p><p>${esc(transfer.justification)}</p><table><thead><tr><th>Material/lote</th><th>Expedido</th><th>Recebido</th><th>Em trânsito</th><th></th></tr></thead><tbody>${transfer.items.map(i => `<tr><td>${esc(i.product)}<br><small>${esc(i.lotNumber || 'Sem lote')}</small></td><td>${i.shipped} ${esc(i.unit)}</td><td>${i.received} ${esc(i.unit)}</td><td><b>${i.inTransit} ${esc(i.unit)}</b></td><td>${i.inTransit > 0 && ['IN_TRANSIT','PARTIALLY_RECEIVED'].includes(transfer.status) ? `<button data-receive="${i.id}">Conferir</button>` : ''}</td></tr>`).join('')}</tbody></table><div class="actions">${transfer.status === 'AWAITING_SHIPMENT' ? '<button class="primary-button" data-ship>Confirmar expedição</button><button data-cancel>Cancelar</button>' : ''}</div><h3>Histórico</h3><ol>${transfer.history.map(h => `<li><b>${esc(h.type)}</b> — ${esc(h.actor)} <time>${new Date(h.occurredAt).toLocaleString()}</time>${h.reason ? `<p>${esc(h.reason)}</p>` : ''}</li>`).join('')}</ol>`;
    root.querySelector('[data-ship]')?.addEventListener('click', () => action('Expedir transferência', 'A quantidade sairá da origem e passará a constar somente em trânsito.', [{ name: 'reason', label: 'Responsável e observação', type: 'textarea' }], async d => request(`${api}/transfers/${id}/ship`, { method: 'POST', body: JSON.stringify({ reason: d.reason, version: transfer.version, idempotencyKey: key() }) }), () => showTransfer(id)));
    root.querySelector('[data-cancel]')?.addEventListener('click', () => action('Cancelar transferência', 'Somente uma transferência sem expedição pode ser cancelada.', [{ name: 'reason', label: 'Justificativa', type: 'textarea' }], async d => request(`${api}/transfers/${id}/cancel`, { method: 'POST', body: JSON.stringify({ reason: d.reason, version: transfer.version, idempotencyKey: key() }) }), () => showTransfer(id)));
    root.querySelectorAll('[data-receive]').forEach(b => b.onclick = () => action('Receber no destino', 'O material bom será disponibilizado no destino; avariado/bloqueado continuará indisponível.', [{ name: 'quantity', label: 'Quantidade desta conferência', type: 'number' }, { name: 'condition', label: 'Condição', type: 'select', options: ['GOOD', 'DAMAGED', 'BLOCKED'] }, { name: 'occurrence', label: 'Ocorrência / observação', type: 'textarea' }], async d => request(`${api}/transfers/${id}/receive`, { method: 'POST', body: JSON.stringify({ itemId: b.dataset.receive, quantity: Number(d.quantity), condition: d.condition, occurrence: d.occurrence, version: transfer.version, idempotencyKey: key() }) }), () => showTransfer(id)));
  }

  async function loadCounts() {
    const root = document.getElementById('count-list');
    root.innerHTML = '<p>Carregando inventários…</p>';
    try {
      const s = document.querySelector('[data-filter="counts"] [name=status]').value;
      const data = await request(`${api}/counts?status=${encodeURIComponent(s)}`);
      root.innerHTML = data.items.length
        ? `<table><thead><tr><th>Número</th><th>Depósito</th><th>Progresso</th><th>Situação</th><th></th></tr></thead><tbody>${data.items.map(x => `<tr><td>#${x.number}</td><td>${esc(x.warehouse)}</td><td>${x.countedItems}/${x.totalItems}</td><td><span class="status-badge">${esc(x.status)}</span></td><td><button data-count="${x.id}">Abrir</button></td></tr>`).join('')}</tbody></table>`
        : '<p class="empty-state">Nenhum inventário encontrado.</p>';
      root.querySelectorAll('[data-count]').forEach(b => b.onclick = () => showCount(b.dataset.count));
    } catch (err) { root.innerHTML = `<p role="alert">${esc(err.message)}</p>`; }
  }

  async function showCount(id) {
    count = await request(`${api}/counts/${id}`);
    const root = document.getElementById('count-detail');
    root.hidden = false;
    root.innerHTML = `<header><h2>Inventário #${count.number} — ${esc(count.warehouse)}</h2><span class="status-badge">${esc(count.status)}</span></header><p><b>Estratégia:</b> bloqueio integral das movimentações no escopo.</p>${count.status === 'PLANNED' ? '<button class="primary-button" data-open-count>Abrir contagem e bloquear</button>' : ''}<div class="count-grid">${count.items.map(i => `<article><h3>${esc(i.product)}</h3><p>Lote: ${esc(i.lotNumber || 'sem lote')} · Unidade: ${esc(i.unit)}</p>${i.referenceQuantity === null ? '<p>Saldo esperado oculto (contagem cega).</p>' : `<p>Referência: <b>${i.referenceQuantity}</b> · Reserva: ${i.reserved}</p>`}<p>Estado: ${i.entries.length ? `contado (${i.entries.length} rodada(s))` : '<b>não contado</b>'}</p>${count.status === 'COUNTING' ? `<button data-entry="${i.id}" data-unit="${esc(i.unit)}">Registrar ${i.entries.length ? 'recontagem' : 'contagem'}</button>` : ''}${i.entries.map(e => `<button data-reconcile="${i.id}" data-entry-id="${e.id}">Aceitar ${e.quantity} (${e.round}ª)</button>`).join('')}${i.difference == null ? '' : `<p class="difference"><b>Diferença:</b> ${i.difference} ${esc(i.unit)}</p>`}</article>`).join('')}</div>${count.status === 'RECONCILING' ? '<button class="primary-button" data-approve>Aprovar ajustes e concluir</button>' : ''}`;
    root.querySelector('[data-open-count]')?.addEventListener('click', () => action('Abrir contagem', 'O saldo será fotografado e APIs, integrações e operações ficarão bloqueadas no escopo.', [{ name: 'reason', label: 'Orientação à equipe', type: 'textarea' }], d => request(`${api}/counts/${id}/open`, { method: 'POST', body: JSON.stringify({ reason: d.reason, version: count.version }) }), () => showCount(id)));
    root.querySelectorAll('[data-entry]').forEach(b => b.onclick = () => action('Registrar contagem', 'Digite 0 para material ausente. Não envie o campo vazio.', [{ name: 'quantity', label: `Quantidade (${b.dataset.unit})`, type: 'number' }, { name: 'note', label: 'Observação', type: 'textarea' }], d => request(`${api}/counts/${id}/entries`, { method: 'POST', body: JSON.stringify({ itemId: b.dataset.entry, quantity: d.quantity === '' ? null : Number(d.quantity), unit: b.dataset.unit, note: d.note, evidenceUrl: null, version: count.version }) }), () => showCount(id)));
    root.querySelectorAll('[data-reconcile]').forEach(b => b.onclick = () => action('Reconciliar item', 'Escolha aceitar/ajustar ou solicitar nova contagem; o histórico anterior não será sobrescrito.', [{ name: 'decision', label: 'Decisão', type: 'select', options: ['ADJUST', 'ACCEPT', 'RECOUNT', 'NO_ACTION'] }, { name: 'justification', label: 'Justificativa', type: 'textarea' }], d => request(`${api}/counts/${id}/reconcile`, { method: 'POST', body: JSON.stringify({ itemId: b.dataset.reconcile, entryId: b.dataset.entryId, decision: d.decision, justification: d.justification, version: count.version }) }), () => showCount(id)));
    root.querySelector('[data-approve]')?.addEventListener('click', () => action('Aprovar ajustes', 'Cada diferença gerará no máximo um movimento vinculado ao inventário. Esta ação não apaga reservas.', [{ name: 'reason', label: 'Justificativa da aprovação', type: 'textarea' }], d => request(`${api}/counts/${id}/approve`, { method: 'POST', body: JSON.stringify({ reason: d.reason, version: count.version }) }), () => showCount(id)));
  }

  function action(title, effect, fields, submit, done) {
    const dialog = document.getElementById('stock-action-dialog'), form = document.getElementById('stock-action-form');
    form.querySelector('[data-title]').textContent = title;
    form.querySelector('[data-effect]').textContent = effect;
    form.querySelector('[data-fields]').innerHTML = fields.map(f => f.type === 'select' ? `<label>${esc(f.label)} *<select name="${f.name}" required>${f.options.map(o => `<option>${o}</option>`).join('')}</select></label>` : f.type === 'textarea' ? `<label class="wide">${esc(f.label)} *<textarea name="${f.name}" required maxlength="1000"></textarea></label>` : `<label>${esc(f.label)} *<input name="${f.name}" type="number" min="0" step="0.001" required></label>`).join('');
    form.onsubmit = async e => {
      e.preventDefault();
      const message = form.querySelector('.form-message');
      try {
        await submit(Object.fromEntries(new FormData(form)));
        dialog.close();
        await done();
      } catch (err) { message.textContent = err.message; }
    };
    dialog.showModal();
  }

  // --- AI STOCK ASSISTANT HANDLING ---
  let assistantAbortController = null;
  document.querySelectorAll('.chip[data-ask]').forEach(chip => {
    chip.addEventListener('click', () => {
      const input = document.getElementById('assistant-query');
      if (input) {
        input.value = chip.dataset.ask;
        document.getElementById('assistant-form')?.requestSubmit();
      }
    });
  });

  const assistantForm = document.getElementById('assistant-form');
  const assistantLoading = document.getElementById('assistant-loading');
  const assistantResult = document.getElementById('assistant-result');
  const assistantCancel = document.getElementById('assistant-cancel');

  assistantCancel?.addEventListener('click', () => {
    if (assistantAbortController) {
      assistantAbortController.abort();
      assistantAbortController = null;
      if (assistantLoading) assistantLoading.hidden = true;
      if (assistantCancel) assistantCancel.hidden = true;
      if (assistantResult) {
        assistantResult.hidden = false;
        assistantResult.innerHTML = '<p class="empty-state">Consulta cancelada pelo usuário.</p>';
      }
    }
  });

  assistantForm?.addEventListener('submit', async e => {
    e.preventDefault();
    const queryInput = document.getElementById('assistant-query');
    const question = queryInput?.value?.trim();
    if (!question) return;

    if (assistantAbortController) {
      assistantAbortController.abort();
    }
    assistantAbortController = new AbortController();

    if (assistantLoading) assistantLoading.hidden = false;
    if (assistantCancel) assistantCancel.hidden = false;
    if (assistantResult) assistantResult.hidden = true;

    try {
      // Chave da intenção: gerada quando o usuário envia a pergunta. Uma re-tentativa da MESMA
      // intenção reaproveita a resposta sem novo consumo de cota; perguntar de novo é outra
      // intenção (nova chave) e recalcula com dados frescos.
      const operationKey = typeof crypto !== 'undefined' && crypto.randomUUID
        ? crypto.randomUUID()
        : `${Date.now()}-${Math.random().toString(36).slice(2)}`;
      const res = await fetch('/api/v1/inventory/assistant/query', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ question, operationKey }),
        signal: assistantAbortController.signal
      });

      if (!res.ok) {
        throw new Error(await problem(res));
      }

      const data = await res.json();
      renderAssistantResponse(data);
    } catch (err) {
      if (err.name === 'AbortError') return;
      if (assistantResult) {
        assistantResult.hidden = false;
        assistantResult.innerHTML = `<div class="assistant-error" role="alert"><p><strong>Não foi possível obter resposta:</strong> ${esc(err.message)}</p></div>`;
      }
    } finally {
      assistantAbortController = null;
      if (assistantLoading) assistantLoading.hidden = true;
      if (assistantCancel) assistantCancel.hidden = true;
    }
  });

  function renderAssistantResponse(resp) {
    if (!assistantResult) return;
    assistantResult.hidden = false;

    let html = `
      <div class="assistant-card">
        <div class="assistant-meta">
          <span class="status-badge">Provedor: ${esc(resp.sourceProvider || 'IA')}</span>
          <span class="status-badge">Modelo: ${esc(resp.model || 'Padrão')}</span>
          <span class="status-badge">Tokens: ${resp.totalTokens || 0}</span>
        </div>
        <div class="assistant-body">
          <p class="assistant-answer">${esc(resp.answer).replace(/\n/g, '<br/>')}</p>
        </div>`;

    if (resp.data && resp.data.length > 0) {
      html += `
        <div class="assistant-table-wrap">
          <h4>Saldos consultados (${resp.data.length} de ${resp.totalRecords || resp.data.length})</h4>
          <table>
            <thead>
              <tr><th>Produto</th><th>SKU</th><th>Disponível</th><th>Mínimo</th><th>Reservado</th></tr>
            </thead>
            <tbody>
              ${resp.data.map(b => `
                <tr class="${b.available < b.minimum ? 'critical-row' : ''}">
                  <td>${esc(b.productName)}</td>
                  <td>${esc(b.sku)}</td>
                  <td><b>${b.available} ${esc(b.unit)}</b></td>
                  <td>${b.minimum} ${esc(b.unit)}</td>
                  <td>${b.reserved} ${esc(b.unit)}</td>
                </tr>
              `).join('')}
            </tbody>
          </table>
        </div>`;
    }

    if (resp.suggestsReplenishment && resp.draft) {
      const d = resp.draft;
      html += `
        <div class="replenishment-draft-box">
          <h4>💡 Sugestão de Reposição Identificada</h4>
          <p><strong>Produto:</strong> ${esc(d.product)} (${esc(d.sku)})</p>
          <p><strong>Quantidade proposta:</strong> ${d.quantity} ${esc(d.unit)}</p>
          <p><strong>Justificativa:</strong> ${esc(d.justification)}</p>
          <div class="actions">
            <button type="button" class="primary-button" id="btn-create-request-from-draft">
              Preencher Nova Requisição
            </button>
          </div>
        </div>`;
    }

    html += `
      <div class="assistant-feedback">
        <small>Esta resposta foi útil?</small>
        <button type="button" class="feedback-btn" onclick="this.parentElement.innerHTML='<small>Obrigado pelo feedback!</small>'">👍 Sim</button>
        <button type="button" class="feedback-btn" onclick="this.parentElement.innerHTML='<small>Obrigado, continuaremos aprimorando.</small>'">👎 Insuficiente</button>
      </div>
    </div>`;

    assistantResult.innerHTML = html;

    const draftBtn = document.getElementById('btn-create-request-from-draft');
    if (draftBtn && resp.draft) {
      draftBtn.addEventListener('click', () => {
        document.querySelector('[data-stock-tab="requests"]')?.click();
        const dialog = document.getElementById('request-dialog');
        if (dialog) {
          dialog.showModal();
          const form = document.getElementById('request-form');
          if (form) {
            if (form.elements.productId) form.elements.productId.value = resp.draft.productId;
            if (form.elements.quantity) form.elements.quantity.value = resp.draft.quantity;
            if (form.elements.unit) form.elements.unit.value = resp.draft.unit;
            if (form.elements.purpose) form.elements.purpose.value = 'OPERATION';
            if (form.elements.priority) form.elements.priority.value = 'HIGH';
            if (form.elements.neededOn) form.elements.neededOn.value = new Date(Date.now() + 86400000 * 3).toISOString().split('T')[0];
            if (form.elements.urgencyReason) form.elements.urgencyReason.value = resp.draft.justification;
          }
        }
      });
    }
  }
})();
