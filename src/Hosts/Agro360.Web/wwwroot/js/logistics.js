(() => {
    const api = document.querySelector('meta[name="api-base"]')?.content?.replace(/\/$/, '') || '';
    const token = () => sessionStorage.getItem('agro360.accessToken');
    const content = document.querySelector('#logistics-content');
    const indicators = document.querySelector('#logistics-indicators');
    const dispositionDialog = document.querySelector('#return-disposition-dialog');
    const dispositionForm = document.querySelector('#return-disposition-form');
    const decisionSelect = document.querySelector('#disposition-decision');
    const quantityInput = document.querySelector('#disposition-quantity');
    const unitInput = document.querySelector('#disposition-unit');
    const costInput = document.querySelector('#disposition-cost');
    const lossFields = document.querySelector('#disposition-loss-fields');
    const itemInfo = document.querySelector('#disposition-item-info');

    let currentReturnForDisposition = null;
    let activeView = 'overview';

    const request = async (path, options = {}) => {
        const headers = {
            Authorization: `Bearer ${token()}`,
            ...(options.body ? { 'Content-Type': 'application/json' } : {}),
            ...options.headers
        };
        try {
            const response = await fetch(`${api}${path}`, {
                ...options,
                headers,
                cache: 'no-store'
            });

            if (!response.ok) {
                const errData = await response.json().catch(() => null);
                const message = errData?.detail || errData?.message || `Operação não concluída (HTTP ${response.status}).`;
                const err = new Error(message);
                err.status = response.status;
                err.code = errData?.code;
                throw err;
            }

            if (response.status === 204) return null;
            return response.json();
        } catch (error) {
            if (error.status === undefined) {
                // Erro de rede ou TypeError do fetch
                error.isNetwork = true;
            }
            throw error;
        }
    };

    const value = (x, ...names) => names.map(n => x[n] ?? x[n[0].toUpperCase() + n.slice(1)]).find(v => v !== undefined);

    const hasPermission = (code) => {
        const perms = new Set(((window.agro360Session?.permissions) ?? [])
            .map(p => String(p ?? '').toLowerCase().replace(/^agro360\./, '').replaceAll('_', '.')));
        return perms.has(code);
    };
    const civilToday = () => { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`; };

    function updateBreadcrumb(tabName) {
        const bc = document.querySelector('#page-breadcrumb span');
        if (!bc) return;
        if (tabName === 'returns') {
            bc.textContent = 'Logística / Retornos';
        } else {
            bc.textContent = 'Expedição e Entrega';
        }
    }

    async function loadIndicators() {
        try {
            const x = await request('/api/logistics/trips/fulfillment/indicators');
            const cards = [
                ['awaitingPicking', 'Pedidos aguardando separação', 'queue'],
                ['ready', 'Expedições prontas', 'shipments'],
                ['tripsInProgress', 'Viagens em andamento', 'trips'],
                ['late', 'Entregas atrasadas', 'deliveries'],
                ['partial', 'Entregas parciais', 'deliveries'],
                ['refusals', 'Recusas', 'deliveries'],
                ['returnsAwaitingQuality', 'Retornos em conferência', 'returns'],
                ['untreatedDivergences', 'Divergências sem tratamento', 'picking']
            ];
            indicators.innerHTML = cards.map(([key, label, view]) =>
                `<button class="indicator" data-view="${view}"><strong>${value(x, key) ?? 0}</strong>${label}</button>`
            ).join('');

            indicators.querySelectorAll('.indicator').forEach(b => {
                b.addEventListener('click', () => {
                    const targetView = b.dataset.view;
                    switchView(targetView);
                });
            });
        } catch (err) {
            indicators.innerHTML = '<p class="form-message" role="alert">Indicadores temporariamente indisponíveis.</p>';
            window.agro360Feedback?.handleError(err, 'Falha ao carregar indicadores');
        }
    }

    async function loadQueue(form) {
        content.innerHTML = '<p class="empty-state">Carregando pedidos aptos…</p>';
        try {
            const q = new URLSearchParams(new FormData(form || document.querySelector('#queue-filters')));
            [...q].forEach(([k, v]) => { if (!v) q.delete(k); });
            const page = await request(`/api/logistics/trips/fulfillment/queue?${q}`);
            const rows = value(page, 'items') || [];
            const currentPage = Number(value(page, 'page') || 1);
            const total = Number(value(page, 'total') || 0);
            const pageSize = Number(value(page, 'pageSize', 'page_size') || 20);
            content.innerHTML = rows.length ? `
                <table class="logistics-table">
                    <thead>
                        <tr>
                            <th>Pedido</th>
                            <th>Cliente</th>
                            <th>Prazo</th>
                            <th>Situação</th>
                            <th>Reservada</th>
                            <th>Saldo autorizado</th>
                            <th>Ação</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${rows.map(x => `
                            <tr>
                                <td><strong>${escapeHtml(value(x, 'order_number', 'orderNumber'))}</strong></td>
                                <td>${escapeHtml(value(x, 'customer'))}</td>
                                <td>${escapeHtml(value(x, 'expected_delivery', 'expectedDelivery') ?? 'Pendente')}</td>
                                <td><span class="status-pill">${escapeHtml(value(x, 'status'))}</span></td>
                                <td>${escapeHtml(value(x, 'quantity_reserved', 'quantityReserved') ?? 0)}</td>
                                <td>${escapeHtml(value(x, 'quantity_pending', 'quantityPending'))}</td>
                                <td><button type="button" class="secondary-button" data-order-detail="${escapeHtml(value(x, 'order_id', 'orderId'))}">Abrir detalhe</button></td>
                            </tr>
                        `).join('')}
                    </tbody>
                </table><nav class="pagination" aria-label="Paginação"><button type="button" class="secondary-button" data-page="${currentPage - 1}" ${currentPage <= 1 ? 'disabled' : ''}>Anterior</button><span>Página ${currentPage} de ${Math.max(1, Math.ceil(total / pageSize))} · ${total} pedido(s)</span><button type="button" class="secondary-button" data-page="${currentPage + 1}" ${currentPage * pageSize >= total ? 'disabled' : ''}>Próxima</button></nav>` : total > 0 ? `<div class="empty-state"><p>A página ${currentPage} ficou fora do intervalo após uma atualização. O total continua sendo ${total}.</p><button type="button" class="secondary-button" data-page="${Math.ceil(total/pageSize)}">Voltar à última página válida</button></div>` : '<p class="empty-state">Nenhum pedido apto para os filtros. Ajuste os filtros ou confirme se o pedido foi aprovado e possui saldo pendente.</p>';
            content.querySelectorAll('[data-page]').forEach(button => button.addEventListener('click', () => { const filter = document.querySelector('#queue-filters'); filter.elements.page.value = button.dataset.page; loadQueue(filter); }));
            content.querySelectorAll('[data-order-detail]').forEach(button => button.addEventListener('click', () => loadOrderDetail(button.dataset.orderDetail)));
        } catch (err) {
            renderErrorState(err, 'Fila de pedidos');
        }
    }

    async function loadOrderDetail(orderId) {
        content.innerHTML = '<p class="empty-state">Carregando resumo, itens, reservas, expedições e histórico…</p>';
        try {
            const data = await request(`/api/logistics/trips/fulfillment/orders/${orderId}`);
            const order = value(data, 'order') || {}, items = value(data, 'items') || [], reservations = value(data, 'reservations') || [], shipments = value(data, 'shipments') || [], history = value(data, 'history') || [], schedules = value(data, 'schedules') || [], scheduleItems = value(data, 'scheduleItems', 'schedule_items') || [];
            content.innerHTML = `<div style="display:flex;gap:.5rem;margin-bottom:1rem">
                <a href="/Commercial?view=orders&orderId=${orderId}" class="secondary-button" id="back-to-commercial">← Voltar ao Comercial</a>
                <button type="button" class="secondary-button" id="back-to-queue">← Voltar à fila</button>
              </div>
              <section><h2>Resumo</h2><dl><dt>Pedido</dt><dd>${escapeHtml(value(order,'order_number','orderNumber'))}</dd><dt>Cliente</dt><dd>${escapeHtml(value(order,'customer'))}</dd><dt>Origem</dt><dd>${escapeHtml(value(order,'proposal_number','proposalNumber') || 'Pedido direto')}${value(order,'proposal_version','proposalVersion') ? ` · versão ${escapeHtml(value(order,'proposal_version','proposalVersion'))}` : ''}</dd><dt>Moeda e condições</dt><dd>${escapeHtml(value(order,'currency') || 'Não informada')} · ${escapeHtml(value(order,'payment_terms','paymentTerms') || 'Não informadas')}</dd></dl></section>
              <section><h2>Compromissos de Entrega (Programação Comercial)</h2><p class="form-hint">Abrir o compromisso não reserva estoque. A reserva só ocorre na confirmação.</p>${renderOperationalTable(schedules, [['schedule_number','Nº Programação'],['planned_date','Data Prevista'],['destination','Destino'],['responsible','Responsável'],['status','Situação'],['actions','Ações']], row => {
                  const st = value(row, 'status');
                  const sId = value(row, 'id');
                  const lines = scheduleItems.filter(item => String(value(item, 'schedule_id', 'scheduleId')) === String(sId));
                  const attendable = lines.some(item => Number(value(item, 'attendable_quantity', 'attendableQuantity')) > 0);
                  if (st !== 'CANCELLED' && st !== 'DELIVERED' && attendable) {
                      return `<button type="button" class="primary-button" data-fulfill-schedule="${escapeHtml(sId)}">Atender compromisso</button>`;
                  }
                  return `<span class="status-pill">${escapeHtml(st || 'Sem saldo atendível')}</span>`;
              })}</section>
              <section><h2>Itens</h2><p class="form-hint">Pendente = pedida − cancelada − expedida. Reserva ativa e separação são subconjuntos do pendente e não são descontadas novamente.</p>${renderOperationalTable(items, [['product','Produto'],['unit','Unidade'],['ordered_quantity','Pedida'],['cancelled_quantity','Cancelada'],['reserved_quantity','Reservada ativa'],['picked_quantity','Separada'],['dispatched_quantity','Expedida'],['pending_quantity','Pendente'],['actions','Ações']], row => `<button type="button" class="secondary-button" data-reserve="${escapeHtml(value(row,'id'))}">Reservar</button> <button type="button" class="secondary-button" data-cancel-item="${escapeHtml(value(row,'id'))}" data-version="${escapeHtml(value(row,'fulfillment_version','fulfillmentVersion'))}">Cancelar saldo</button>` )}</section>
              <section><h2>Reservas</h2>${renderOperationalTable(reservations, [['warehouse','Depósito'],['lot_number','Lote'],['quantity','Original'],['active_quantity','Ativa'],['consumed_quantity','Consumida'],['released_quantity','Liberada'],['status','Estado'],['actions','Ações']], row => value(row,'status') === 'ACTIVE' ? `<button type="button" class="secondary-button" data-release="${escapeHtml(value(row,'id'))}" data-version="${escapeHtml(value(row,'version'))}" data-max="${escapeHtml(value(row,'active_quantity','activeQuantity'))}">Liberar</button>` : '—')}</section>
              <section><h2>Expedições</h2>${renderOperationalTable(shipments, [['number','Número'],['status','Estado'],['dispatched_at','Confirmada em'],['actions','Ações']], row => value(row,'status') === 'CHECKED' ? `<button type="button" class="secondary-button" data-reopen="${escapeHtml(value(row,'id'))}" data-version="${escapeHtml(value(row,'version'))}">Reabrir preparação</button> <button type="button" class="primary-button" data-dispatch="${escapeHtml(value(row,'id'))}" data-version="${escapeHtml(value(row,'version'))}">Expedir</button>` : value(row,'status') === 'PREPARING' ? `<button type="button" class="secondary-button" data-prepare="${escapeHtml(value(row,'id'))}">Separar e conferir</button>` : escapeHtml(value(row,'status')))}</section>
              <section><h2>Histórico</h2>${renderOperationalTable(history, [['event_type','Evento'],['created_at','Data']])}</section>`;
            document.querySelector('#back-to-queue')?.addEventListener('click', () => loadQueue());
            content.querySelectorAll('[data-fulfill-schedule]').forEach(b => {
                b.addEventListener('click', () => {
                    const scheduleId = b.dataset.fulfillSchedule;
                    const schedule = schedules.find(s => String(value(s, 'id')) === scheduleId);
                    const lines = scheduleItems.filter(item => String(value(item, 'schedule_id', 'scheduleId')) === scheduleId && Number(value(item, 'attendable_quantity', 'attendableQuantity')) > 0);
                    if (!schedule || !lines.length) {
                        window.agro360Feedback?.toast('warning', 'Sem saldo', 'Este compromisso não tem quantidade ainda atendível.');
                        return;
                    }
                    showScheduleFulfillment(schedule, lines, order, orderId);
                });
            });
            content.querySelectorAll('[data-reserve]').forEach(b => b.addEventListener('click', () => showReserveForm(b.dataset.reserve, order, items.find(x => String(value(x,'id')) === b.dataset.reserve), orderId)));
            content.querySelectorAll('[data-release]').forEach(b => b.addEventListener('click', () => releaseReservation(b.dataset.release, Number(b.dataset.version), Number(b.dataset.max), orderId)));
            content.querySelectorAll('[data-cancel-item]').forEach(b => b.addEventListener('click', () => cancelItem(b.dataset.cancelItem, Number(b.dataset.version), orderId)));
            content.querySelectorAll('[data-dispatch]').forEach(b => b.addEventListener('click', () => dispatchShipment(b.dataset.dispatch, Number(b.dataset.version), orderId)));
            content.querySelectorAll('[data-reopen]').forEach(b => b.addEventListener('click', () => reopenShipment(b.dataset.reopen, Number(b.dataset.version), orderId)));
            content.querySelectorAll('[data-prepare]').forEach(b => b.addEventListener('click', () => showPreparation(b.dataset.prepare, orderId)));
        } catch (err) { renderErrorState(err, 'Detalhe operacional do pedido'); }
    }

    function renderOperationalTable(rows, columns, actionRenderer) {
        if (!rows.length) return '<p class="empty-state">Nenhum registro vinculado nesta seção.</p>';
        return `<div class="table-scroll"><table class="logistics-table"><thead><tr>${columns.map(([,label]) => `<th>${label}</th>`).join('')}</tr></thead><tbody>${rows.map(row => `<tr>${columns.map(([key]) => `<td>${key === 'actions' ? actionRenderer?.(row) || '—' : escapeHtml(value(row,key,key.replace(/_([a-z])/g,(_,x)=>x.toUpperCase())) ?? '—')}</td>`).join('')}</tr>`).join('')}</tbody></table></div>`;
    }

    async function showScheduleFulfillment(schedule, lines, order, orderId) {
        const scheduleId = value(schedule, 'id');
        const enriched = [];
        for (const line of lines) {
            const orderItemId = value(line, 'order_item_id', 'orderItemId');
            const lots = await request(`/api/logistics/trips/fulfillment/order-items/${orderItemId}/eligible-lots`);
            enriched.push({ line, lots: lots || [] });
        }
        const host = document.createElement('div');
        host.className = 'operation-review';
        host.innerHTML = `<h3>Atender compromisso ${escapeHtml(value(schedule, 'schedule_number', 'scheduleNumber'))}</h3>
            <p>Selecione um ou mais itens. Vários lotes do mesmo depósito podem compor a quantidade. Reservar não é saída física. Lotes de depósitos diferentes exigem outra confirmação.</p>
            <form>
            ${enriched.map((entry, index) => {
                const line = entry.line;
                const attendable = value(line, 'attendable_quantity', 'attendableQuantity');
                return `<fieldset>
                    <legend><label><input type="checkbox" name="take" value="${index}" checked> ${escapeHtml(value(line, 'product'))} · ${escapeHtml(value(line, 'unit'))}</label></legend>
                    <p>Programada ${escapeHtml(value(line, 'quantity'))} · em preparação ${escapeHtml(value(line, 'preparing_quantity', 'preparingQuantity'))} · expedida ${escapeHtml(value(line, 'dispatched_quantity', 'dispatchedQuantity'))} · entregue ${escapeHtml(value(line, 'delivered_quantity', 'deliveredQuantity'))} · saldo atendível ${escapeHtml(attendable)}</p>
                    ${entry.lots.length ? `<label>Lote e depósito<select name="lot-${index}" data-lots>${entry.lots.map(lot => `<option value="${escapeHtml(value(lot, 'lot_id', 'lotId'))}" data-warehouse="${escapeHtml(value(lot, 'warehouse_id', 'warehouseId'))}">${escapeHtml(value(lot, 'warehouse'))} · lote ${escapeHtml(value(lot, 'lot_number', 'lotNumber'))} · saldo ${escapeHtml(value(lot, 'eligible_quantity', 'eligibleQuantity'))}</option>`).join('')}</select></label>
                    <label>Quantidade (${escapeHtml(value(line, 'unit'))})<input name="qty-${index}" type="number" min="0.000001" max="${escapeHtml(attendable)}" step="any" value="${escapeHtml(attendable)}" required></label>` : '<p role="status">Nenhum lote aprovado e com saldo para este produto.</p>'}
                </fieldset>`;
            }).join('')}
            <label>Destino<input name="destination" maxlength="200" required value="${escapeHtml(value(schedule, 'destination') || ('Cliente ' + (value(order, 'customer') || '')))}"></label>
            <button class="primary-button">Confirmar reserva</button>
            <button type="button" class="secondary-button" data-dismiss>Cancelar</button>
            <p role="alert"></p>
            </form>`;
        content.prepend(host);
        host.querySelector('[data-dismiss]').onclick = () => host.remove();
        host.querySelector('input,select')?.focus();
        host.querySelector('form').onsubmit = async event => {
            event.preventDefault();
            const form = event.currentTarget;
            const button = form.querySelector('.primary-button');
            const selected = [...form.querySelectorAll('[name=take]:checked')];
            if (!selected.length) { form.querySelector('[role=alert]').textContent = 'Selecione ao menos um item.'; return; }
            const picked = selected.map(box => {
                const index = box.value;
                const entry = enriched[Number(index)];
                const select = form.querySelector(`[name=lot-${index}]`);
                const option = select?.selectedOptions?.[0];
                return {
                    orderItemId: value(entry.line, 'order_item_id', 'orderItemId'),
                    scheduleItemId: value(entry.line, 'id'),
                    stockLotId: option?.value,
                    warehouseId: option?.dataset.warehouse,
                    quantity: Number(form.querySelector(`[name=qty-${index}]`).value),
                    unit: value(entry.line, 'unit'),
                    attendable: Number(value(entry.line, 'attendable_quantity', 'attendableQuantity'))
                };
            });
            if (picked.some(line => !line.stockLotId || !(line.quantity > 0))) {
                form.querySelector('[role=alert]').textContent = 'Cada item selecionado precisa de lote e quantidade positiva.';
                return;
            }
            if (picked.some(line => line.quantity > line.attendable)) {
                form.querySelector('[role=alert]').textContent = 'A quantidade não pode passar do saldo ainda atendível.';
                return;
            }
            const warehouses = [...new Set(picked.map(line => line.warehouseId))];
            if (warehouses.length !== 1) {
                form.querySelector('[role=alert]').textContent = 'Nesta confirmação use lotes do mesmo depósito. Repita a operação para o outro depósito.';
                return;
            }
            const fingerprint = JSON.stringify(picked.map(line => ({ line: line.scheduleItemId, lot: line.stockLotId, quantity: line.quantity })));
            const storageKey = `agro360.fulfillment.schedule.${scheduleId}`;
            let draft = null;
            try { draft = JSON.parse(sessionStorage.getItem(storageKey) || 'null'); } catch { draft = null; }
            if (!draft || draft.fingerprint !== fingerprint) {
                draft = { fingerprint, key: crypto.randomUUID(), number: `SEP-${crypto.randomUUID().slice(0, 8).toUpperCase()}` };
                sessionStorage.setItem(storageKey, JSON.stringify(draft));
            }
            button.disabled = true;
            try {
                await request('/api/logistics/trips/fulfillment', {
                    method: 'POST',
                    body: JSON.stringify({
                        number: draft.number,
                        originWarehouseId: warehouses[0],
                        customerId: value(order, 'customer_id', 'customerId'),
                        destination: form.destination.value,
                        scheduleId,
                        idempotencyKey: draft.key,
                        items: picked.map(line => ({
                            orderItemId: line.orderItemId,
                            stockLotId: line.stockLotId,
                            quantity: line.quantity,
                            pickedQuantity: 0,
                            checkedQuantity: 0,
                            unit: line.unit,
                            divergenceReason: null,
                            scheduleItemId: line.scheduleItemId
                        }))
                    })
                });
                sessionStorage.removeItem(storageKey);
                window.agro360Feedback?.toast('success', 'Reserva criada', 'Saldo comprometido sem saída física.');
                await loadOrderDetail(orderId);
            } catch (err) {
                form.querySelector('[role=alert]').textContent = err.message;
                button.disabled = false;
            }
        };
    }

    async function showReserveForm(itemId, order, item, orderId, scheduleId = null, scheduleItemId = null) {
        const lots = await request(`/api/logistics/trips/fulfillment/order-items/${itemId}/eligible-lots`);
        if (!lots.length) { window.agro360Feedback?.toast('warning','Sem lote elegível','Não há lote aprovado, válido e com saldo livre para este produto.'); return; }
        const host = document.createElement('div'); host.className = 'operation-review';
        host.innerHTML = `<h3>Revisar reserva — ${escapeHtml(value(item,'product'))}</h3><p>Reservar compromete saldo, mas não registra saída física. Separação e conferência serão feitas depois.</p><form><label>Depósito e lote elegível<select name="lot" required>${lots.map(l => `<option value="${escapeHtml(value(l,'lot_id','lotId'))}" data-warehouse="${escapeHtml(value(l,'warehouse_id','warehouseId'))}">${escapeHtml(value(l,'warehouse'))} · lote ${escapeHtml(value(l,'lot_number','lotNumber'))} · saldo ${escapeHtml(value(l,'eligible_quantity','eligibleQuantity'))}</option>`).join('')}</select></label><label>Quantidade (${escapeHtml(value(item,'unit'))})<input name="quantity" type="number" min="0.000001" max="${escapeHtml(value(item,'pending_quantity','pendingQuantity'))}" step="any" required></label><label>Destino<input name="destination" maxlength="200" required value="Cliente ${escapeHtml(value(order,'customer'))}"></label><button class="primary-button">Confirmar reserva</button><button type="button" class="secondary-button" data-dismiss>Cancelar</button><p role="alert"></p></form>`;
        content.prepend(host); host.querySelector('select')?.focus(); host.querySelector('[data-dismiss]').onclick = () => host.remove();
        host.querySelector('form').onsubmit = async e => {
            e.preventDefault();
            const f = e.currentTarget, option = f.lot.selectedOptions[0], button = f.querySelector('.primary-button'),
            intent = {
                number: `SEP-${orderId}-${itemId}`,
                originWarehouseId: option.dataset.warehouse,
                customerId: value(order, 'customer_id', 'customerId'),
                destination: f.destination.value,
                scheduleId: scheduleId || null,
                items: [{
                    orderItemId: itemId,
                    stockLotId: f.lot.value,
                    quantity: Number(f.quantity.value),
                    pickedQuantity:0,checkedQuantity:0,
                    unit: value(item, 'unit'),
                    divergenceReason:null,
                    scheduleItemId: scheduleItemId || null
                }]
            };
            button.disabled = true;
            try {
                await request('/api/logistics/trips/fulfillment', {
                    method: 'POST',
                    body: JSON.stringify({ ...intent, idempotencyKey: operationKey('create',itemId,intent) })
                });
                sessionStorage.removeItem(`agro360.fulfillment.create.${itemId}`);
                window.agro360Feedback?.toast('success', 'Reserva criada', 'Saldo comprometido sem saída física.');
                await loadOrderDetail(orderId);
            } catch (err) {
                f.querySelector('[role=alert]').textContent = err.message;
                button.disabled = false;
            }
        };
    }
    const operationKey=(operation,id,content)=>{const storage=`agro360.fulfillment.${operation}.${id}`,hash=JSON.stringify(content),old=JSON.parse(sessionStorage.getItem(storage)||'null');if(old?.hash===hash)return old.key;const key=crypto.randomUUID();sessionStorage.setItem(storage,JSON.stringify({hash,key}));return key};
    function operationForm(title, fields, review, submit) { const host=document.createElement('div');host.className='operation-review';host.innerHTML=`<h3>${escapeHtml(title)}</h3><p>${escapeHtml(review)}</p><form>${fields}<button class="primary-button">Confirmar</button><button type="button" class="secondary-button" data-dismiss>Cancelar</button><p role="alert"></p></form>`;content.prepend(host);const form=host.querySelector('form');host.querySelector('[data-dismiss]').onclick=()=>host.remove();form.querySelector('input,textarea')?.focus();form.onsubmit=async e=>{e.preventDefault();if(!form.reportValidity())return;const button=form.querySelector('.primary-button');button.disabled=true;try{await submit(form);host.remove()}catch(err){form.querySelector('[role=alert]').textContent=err.message;button.disabled=false}}; }
    async function releaseReservation(id, version, max, orderId) { operationForm('Liberar reserva',`<label>Quantidade (máximo ${escapeHtml(max)})<input name="quantity" type="number" min="0.000001" max="${escapeHtml(max)}" step="any" value="${escapeHtml(max)}" required></label><label>Motivo auditável<textarea name="reason" required></textarea></label>`,'A liberação reduz apenas o compromisso; não cria entrada física.',async f=>{const content={quantity:Number(f.quantity.value),reason:f.reason.value.trim(),version};await mutate(`/api/logistics/trips/fulfillment/reservations/${id}/release`,{...content,idempotencyKey:operationKey('release',id,content)},orderId,'Reserva liberada')}); }
    async function cancelItem(id, version, orderId) { operationForm('Cancelar saldo',`<label>Quantidade<input name="quantity" type="number" min="0.000001" step="any" required></label><label>Motivo auditável<textarea name="reason" required></textarea></label>`,'O pedido original e seus valores serão preservados; nenhum efeito financeiro será criado.',async f=>{const content={quantity:Number(f.quantity.value),reason:f.reason.value.trim(),version};await mutate(`/api/logistics/trips/fulfillment/order-items/${id}/cancel`,{...content,idempotencyKey:operationKey('cancel',id,content)},orderId,'Saldo cancelado')}); }
    async function dispatchShipment(id, version, orderId) { const confirmation=await window.agro360Feedback?.confirm({title:'Confirmar saída física',message:'Qualidade, validade, reserva e saldo serão revalidados. O remanescente ativo desta reserva será liberado com auditoria.',confirmText:'Expedir',cancelText:'Voltar'});if(!confirmation||!confirmation.confirmed)return;const content={version};await mutate(`/api/logistics/trips/fulfillment/${id}/dispatch`,{...content,idempotencyKey:operationKey('dispatch',id,content)},orderId,'Saída confirmada'); }
    async function reopenShipment(id, version, orderId) { const detail=await request(`/api/logistics/trips/fulfillment/${id}`),items=value(detail,'items')||[];operationForm('Reabrir preparação',`<fieldset><legend>Itens a desfazer</legend>${items.map(i=>`<label><input type="checkbox" name="items" value="${escapeHtml(value(i,'id'))}" checked> ${escapeHtml(value(i,'product'))} · ${escapeHtml(value(i,'checked_quantity','checkedQuantity'))} ${escapeHtml(value(i,'unit'))}</label>`).join('')}</fieldset><label>Motivo auditável<textarea name="reason" required></textarea></label>`,'As quantidades selecionadas voltarão a zero sem liberar estoque e sem apagar o histórico.',async f=>{const itemIds=[...f.querySelectorAll('[name=items]:checked')].map(x=>x.value);if(!itemIds.length)throw Error('Selecione ao menos um item.');const content={reason:f.reason.value.trim(),expectedVersion:version,itemIds};await mutate(`/api/logistics/trips/fulfillment/${id}/reopen`,{...content,idempotencyKey:operationKey('reopen',id,content)},orderId,'Preparação reaberta')}); }
    async function showPreparation(id, orderId) { const data=await request(`/api/logistics/trips/fulfillment/${id}`), items=value(data,'items')||[]; const host=document.createElement('div'); host.className='operation-review'; host.innerHTML=`<h3>Separação e conferência</h3><p>Registre cada etapa. Conferida não pode exceder separada; divergência exige motivo somente ao concluir a conferência.</p>${items.map(i=>`<form data-preparation="${escapeHtml(value(i,'id'))}"><strong>${escapeHtml(value(i,'product'))} · lote ${escapeHtml(value(i,'lot_number','lotNumber'))}</strong><label>Separada<input name="picked" type="number" min="0" max="${escapeHtml(value(i,'reserved_quantity','reservedQuantity'))}" step="any" value="${escapeHtml(value(i,'picked_quantity','pickedQuantity'))}" required></label><label>Conferida<input name="checked" type="number" min="0" step="any" value="${escapeHtml(value(i,'checked_quantity','checkedQuantity'))}" required></label><label><input name="complete" type="checkbox" ${value(i,'check_completed','checkCompleted')?'checked':''}> Concluir conferência deste item</label><label>Motivo da divergência<textarea name="reason">${escapeHtml(value(i,'divergence_reason','divergenceReason')||'')}</textarea></label><button class="primary-button">Salvar etapa</button><p role="alert"></p><input type="hidden" name="version" value="${escapeHtml(value(i,'version'))}"></form>`).join('')}<button type="button" class="secondary-button" data-dismiss>Fechar</button>`; content.prepend(host); host.querySelector('input')?.focus(); host.querySelector('[data-dismiss]').onclick=()=>host.remove(); host.querySelectorAll('[data-preparation]').forEach(f=>f.onsubmit=async e=>{e.preventDefault(); const button=f.querySelector('button'),intent={pickedQuantity:Number(f.picked.value),checkedQuantity:Number(f.checked.value),divergenceReason:f.reason.value||null,version:Number(f.version.value),completeCheck:f.complete.checked};button.disabled=true;try{await request(`/api/logistics/trips/fulfillment/items/${f.dataset.preparation}/prepare`,{method:'POST',body:JSON.stringify({...intent,idempotencyKey:operationKey('prepare',f.dataset.preparation,intent)})});sessionStorage.removeItem(`agro360.fulfillment.prepare.${f.dataset.preparation}`);await loadOrderDetail(orderId)}catch(err){f.querySelector('[role=alert]').textContent=err.message;button.disabled=false}}); }
    async function mutate(path, payload, orderId, success) { try { await request(path,{method:'POST',body:JSON.stringify(payload)}); window.agro360Feedback?.toast('success',success,'Estado canônico atualizado.'); await loadOrderDetail(orderId); } catch(err) { window.agro360Feedback?.handleError(err,success); throw err; } }

    function renderErrorState(err, contextTitle) {
        if (err.status === 401) {
            content.innerHTML = '<div class="empty-state" role="alert"><h3>Sessão expirada (401)</h3><p>Faça login novamente para consultar os registros de logística.</p></div>';
        } else if (err.status === 403) {
            content.innerHTML = '<div class="empty-state" role="alert"><h3>Acesso não autorizado (403)</h3><p>Seu perfil não possui permissão para visualizar este módulo logístico.</p></div>';
        } else if (err.isNetwork) {
            content.innerHTML = '<div class="empty-state" role="alert"><h3>Falha de rede</h3><p>Não foi possível comunicar com o servidor. Verifique sua conexão e tente novamente.</p></div>';
        } else {
            const cleanMsg = (err.message || 'Erro inesperado').replace(/[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}/g, '').trim();
            content.innerHTML = `<div class="empty-state" role="alert"><h3>Falha operacional</h3><p>${escapeHtml(cleanMsg)}</p></div>`;
        }
        window.agro360Feedback?.handleError(err, contextTitle);
    }

    async function loadReturns() {
        content.innerHTML = '<p class="empty-state">Carregando retornos físicos…</p>';
        try {
            const rows = await request('/api/logistics/trips/fulfillment/returns');
            if (!rows || rows.length === 0) {
                content.innerHTML = '<p class="empty-state">Nenhum retorno ou devolução física registrada até o momento.</p>';
                return;
            }

            const hasPendingModel = rows.some(r => {
                const intent = value(r, 'intentStatus', 'intent_status');
                const quality = value(r, 'qualityResult', 'quality_result');
                return intent === 'PENDING_MODEL' || quality === 'PENDING_MODEL';
            });

            if (hasPendingModel) {
                window.agro360Feedback?.toast(
                    'warning',
                    'Modelo de qualidade pendente',
                    'Há devoluções aguardando configuração de modelo de inspeção. A liberação (RELEASE) permanece bloqueada.',
                    'pending-model-toast'
                );
            }

            const bannerHtml = hasPendingModel ? `
                <div class="banner warning-banner" role="alert">
                    <strong>Atenção:</strong> Existem retornos com inspeção aguardando parametrização de modelo de qualidade. Liberações (RELEASE) estão bloqueadas até a aprovação da conformidade.
                </div>
            ` : '';

            const tableHtml = `
                ${bannerHtml}
                <table class="logistics-table">
                    <thead>
                        <tr>
                            <th>Expedição</th>
                            <th>Pedido</th>
                            <th>Cliente</th>
                            <th>Produto</th>
                            <th>Qtd Devolvida</th>
                            <th>Qtd Recebida</th>
                            <th>Qualidade</th>
                            <th>Situação</th>
                            <th>Ações</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${rows.map(r => {
                            const id = value(r, 'id', 'Id');
                            const shipment = value(r, 'shipmentNumber', 'shipment_number') || 'Expedição';
                            const order = value(r, 'orderNumber', 'order_number') || 'Pedido';
                            const customer = value(r, 'customerName', 'customer_name') || 'Cliente';
                            const product = value(r, 'productName', 'product_name') || value(r, 'productCode', 'product_code') || 'Produto';
                            const unit = value(r, 'unit') || '';
                            const quantity = Number(value(r, 'quantity') || 0);
                            const received = Number(value(r, 'receivedQuantity', 'received_quantity') || 0);
                            const status = value(r, 'status') || 'PENDING';
                            const intentStatus = value(r, 'intentStatus', 'intent_status');
                            const qualityResult = value(r, 'qualityResult', 'quality_result');
                            const version = Number(value(r, 'version') || 1);

                            let qualityBadge = '<span class="status-pill info">Sem inspeção</span>';
                            if (qualityResult === 'CONFORMING') {
                                qualityBadge = '<span class="status-pill success">Conforme</span>';
                            } else if (qualityResult === 'NON_CONFORMING') {
                                qualityBadge = '<span class="status-pill danger">Não Conforme</span>';
                            } else if (qualityResult === 'INCONCLUSIVE') {
                                qualityBadge = '<span class="status-pill warning">Inconclusivo</span>';
                            } else if (intentStatus === 'PENDING_MODEL' || qualityResult === 'PENDING_MODEL') {
                                qualityBadge = '<span class="status-pill attention">Sem Modelo</span>';
                            } else if (intentStatus === 'AMBIGUOUS' || qualityResult === 'AMBIGUOUS') {
                                qualityBadge = '<span class="status-pill attention">Ambíguo</span>';
                            } else if (intentStatus === 'STARTED' || intentStatus === 'PENDING') {
                                qualityBadge = '<span class="status-pill info">Em Inspeção</span>';
                            }

                            const canDecide = ['AWAITING_QUALITY', 'BLOCKED', 'PARTIALLY_RECEIVED'].includes(status) && received > 0;
                            const isConforming = qualityResult === 'CONFORMING';
                            const isQualityBlocked = ['NON_CONFORMING', 'INCONCLUSIVE', 'PENDING_MODEL', 'AMBIGUOUS'].includes(qualityResult) || ['PENDING_MODEL', 'AMBIGUOUS'].includes(intentStatus) || !isConforming;

                            const itemJson = JSON.stringify({
                                id,
                                shipment,
                                order,
                                customer,
                                product,
                                unit,
                                quantity,
                                received,
                                status,
                                version,
                                qualityResult,
                                intentStatus,
                                isQualityBlocked,
                                isConforming
                            }).replace(/"/g, '&quot;');

                            return `
                                <tr>
                                    <td><strong>${escapeHtml(shipment)}</strong></td>
                                    <td>${escapeHtml(order)}</td>
                                    <td>${escapeHtml(customer)}</td>
                                    <td>${escapeHtml(product)}</td>
                                    <td>${quantity.toLocaleString('pt-BR')} ${escapeHtml(unit)}</td>
                                    <td>${received.toLocaleString('pt-BR')} ${escapeHtml(unit)}</td>
                                    <td>${qualityBadge}</td>
                                    <td><span class="status-pill">${escapeHtml(status)}</span></td>
                                    <td>
                                        ${canDecide ? `
                                            <button type="button" class="secondary-button btn-decide-return" data-return="${itemJson}">
                                                Destinar
                                            </button>
                                        ` : '—'}
                                    </td>
                                </tr>
                            `;
                        }).join('')}
                    </tbody>
                </table>
            `;

            content.innerHTML = tableHtml;

            content.querySelectorAll('.btn-decide-return').forEach(btn => {
                btn.addEventListener('click', () => {
                    const raw = btn.getAttribute('data-return');
                    if (!raw) return;
                    const item = JSON.parse(raw);
                    openDispositionModal(item);
                });
            });

        } catch (err) {
            renderErrorState(err, 'Retornos físicos');
        }
    }

    async function loadTrips() {
        content.innerHTML = '<p class="empty-state">Carregando programação multimodal…</p>';
        try {
            const rows = await request('/api/logistics/trips');
            content.innerHTML = rows.length ? `<table class="logistics-table"><thead><tr><th>Viagem</th><th>Origem → destino</th><th>Modal</th><th>Previsão</th><th>Situação</th><th></th></tr></thead><tbody>${rows.map(x => `<tr><td><strong>${escapeHtml(value(x,'number'))}</strong></td><td>${escapeHtml(value(x,'origin'))} → ${escapeHtml(value(x,'destination'))}</td><td>${escapeHtml(value(x,'transport_mode','transportMode') || 'Pendente')}</td><td>${escapeHtml(value(x,'planned_start','plannedStart') || 'Não informada')}</td><td><span class="status-pill">${escapeHtml(value(x,'status'))}</span></td><td><button type="button" class="secondary-button" data-trip-detail="${escapeHtml(value(x,'id'))}">Abrir plano</button></td></tr>`).join('')}</tbody></table>` : '<p class="empty-state">Nenhuma viagem programada. A programação não reserva estoque; cargas são vinculadas às expedições conferidas.</p>';
            content.querySelectorAll('[data-trip-detail]').forEach(button => button.addEventListener('click', async () => {
                try {
                    const plan = await request(`/api/logistics/trips/${button.dataset.tripDetail}/plan`);
                    const stops = value(plan, 'stops') || [], legs = value(plan, 'legs') || [], allocations = value(plan, 'allocations') || [];
                    content.innerHTML = `<button type="button" class="secondary-button" id="back-to-trips">← Voltar</button><h2>Plano operacional</h2><p><strong>${stops.length}</strong> paradas · <strong>${legs.length}</strong> trechos · <strong>${allocations.length}</strong> alocações</p><p class="empty-state">Capacidade sem unidade, navegabilidade sem fonte/validade e quantidades sem conciliação permanecem como impedimentos; esta tela não presume conversões nem condições reais.</p>`;
                    document.querySelector('#back-to-trips')?.addEventListener('click', loadTrips);
                } catch (err) { renderErrorState(err, 'Detalhe da viagem'); }
            }));
        } catch (err) { renderErrorState(err, 'Programação de viagens'); }
    }

    function openDispositionModal(item) {
        currentReturnForDisposition = item;
        if (!dispositionDialog) return;

        const maxQty = item.received;
        itemInfo.innerHTML = `<strong>${escapeHtml(item.product)}</strong> do pedido <strong>${escapeHtml(item.order)}</strong> (${escapeHtml(item.customer)}). Saldo recebido: <strong>${maxQty.toLocaleString('pt-BR')} ${escapeHtml(item.unit)}</strong>.`;

        quantityInput.value = maxQty;
        quantityInput.max = maxQty;
        unitInput.value = item.unit || '';
        costInput.value = '';

        // Se a qualidade bloqueia RELEASE, avisa no seletor
        const releaseOption = decisionSelect.querySelector('option[value="RELEASE"]');
        if (releaseOption) {
            if (item.isQualityBlocked) {
                releaseOption.textContent = 'Liberar para estoque (RELEASE - BLOQUEADO POR QUALIDADE)';
            } else {
                releaseOption.textContent = 'Liberar para estoque (RELEASE)';
            }
        }

        decisionSelect.value = item.isQualityBlocked ? 'BLOCK' : 'RELEASE';
        lossFields.hidden = decisionSelect.value !== 'DISPOSE';

        decisionSelect.onchange = () => {
            lossFields.hidden = decisionSelect.value !== 'DISPOSE';
            if (decisionSelect.value === 'RELEASE' && item.isQualityBlocked) {
                window.agro360Feedback?.toast(
                    'warning',
                    'Bloqueio de Qualidade',
                    'O laudo de inspeção não autoriza liberação para estoque. Destinações permitidas: BLOCK (manter bloqueado) ou DISPOSE (perda/descarte).'
                );
            }
        };

        dispositionDialog.showModal();
    }

    dispositionDialog?.querySelectorAll('[data-close]').forEach(btn => {
        btn.addEventListener('click', () => dispositionDialog.close());
    });

    dispositionForm?.addEventListener('submit', async (e) => {
        e.preventDefault();
        if (!currentReturnForDisposition) return;

        const item = currentReturnForDisposition;
        const decision = decisionSelect.value;
        const quantity = Number(quantityInput.value);
        const unit = unitInput.value.trim();
        const cost = costInput.value ? Number(costInput.value) : null;

        if (decision === 'RELEASE' && item.isQualityBlocked) {
            window.agro360Feedback?.toast(
                'error',
                'Operação bloqueada',
                'A liberação (RELEASE) não é permitida sem conformidade comprovada de qualidade.'
            );
            return;
        }

        if (quantity <= 0 || quantity > item.received) {
            window.agro360Feedback?.toast('warning', 'Quantidade inválida', `Informe uma quantidade positiva até o limite recebido de ${item.received}.`);
            return;
        }

        if (decision === 'DISPOSE' && !unit) {
            window.agro360Feedback?.toast('warning', 'Unidade obrigatória', 'Informe a unidade de medida para o registro de perda.');
            unitInput.focus();
            return;
        }

        dispositionDialog.close();

        // Destinação → dialog nativo + motivo obrigatório
        const consequences = {
            RELEASE: 'O saldo do lote será integrado ao estoque disponível.',
            BLOCK: 'O material permanecerá isolado em quarentena sem disponibilidade para venda.',
            DISPOSE: `Será registrada perda definitiva de ${quantity} ${unit || item.unit} sem emissão de duplicatas fiscais.`
        };

        const result = await window.agro360Feedback?.confirm({
            title: `Confirmar Destinação: ${decision}`,
            message: `${consequences[decision]} É obrigatório informar o motivo detalhado desta destinação.`,
            confirmText: 'Confirmar Destinação',
            cancelText: 'Cancelar',
            requireReason: true,
            reasonPlaceholder: 'Descreva a motivação da destinação para fins de auditoria…',
            minReasonLength: 3
        });

        if (!result || !result.confirmed || !result.reason) {
            return;
        }

        const idempotencyKey = crypto.randomUUID();
        const payload = {
            decision,
            quantity,
            reason: result.reason,
            expectedVersion: item.version,
            idempotencyKey,
            unit: decision === 'DISPOSE' ? unit : (unit || null),
            cost: decision === 'DISPOSE' ? cost : null
        };

        try {
            await request(`/api/logistics/trips/fulfillment/returns/${item.id}/decisions`, {
                method: 'POST',
                body: JSON.stringify(payload)
            });

            window.agro360Feedback?.toast('success', 'Destinação concluída', `Retorno destinado com sucesso (${decision}).`);
            await refresh();
        } catch (err) {
            window.agro360Feedback?.handleError(err, 'Falha ao registrar destinação');
        }
    });

    async function loadSchedules(filterParams = {}) {
        if (!hasPermission('commercial.read')) {
            content.innerHTML = '<div class="empty-state" role="alert"><h3>Permissão necessária</h3><p>A carteira de compromissos exibe dados comerciais e requer a permissão <code>commercial.read</code>. Solicite ao administrador que a inclua no seu perfil ou abra o pedido pela área Comercial.</p></div>';
            return;
        }
        content.innerHTML = '<p class="empty-state">Carregando carteira de compromissos operacionais…</p>';
        try {
            const q = new URLSearchParams(filterParams);
            [...q].forEach(([k, v]) => { if (!v) q.delete(k); });
            const data = await request(`/api/commercial/schedules?${q}`);
            const rows = value(data, 'items') || [];
            const indicatorsData = value(data, 'indicators') || {};
            const currentPage = Number(value(data, 'page') || 1);
            const total = Number(value(data, 'total') || 0);
            const pageSize = Number(value(data, 'pageSize') || 20);

            const indCards = [
                ['totalOpen', 'Compromissos em Aberto'],
                ['late', 'Atrasados'],
                ['duePeriod', 'Vencendo no Período'],
                ['partiallyDelivered', 'Parcialmente Atendidos'],
                ['unassigned', 'Sem Responsável'],
                ['completed', 'Concluídos']
            ];

            const indHtml = `
                <div class="logistics-indicators" style="margin-bottom:1rem">
                    ${indCards.map(([k, label]) => `
                        <div class="indicator${k === 'late' && (indicatorsData[k] || 0) > 0 ? ' is-late' : ''}">
                            <strong>${indicatorsData[k] ?? 0}</strong>
                            ${label}
                        </div>
                    `).join('')}
                </div>`;

            const filterToolbar = `
                <form class="toolbar" id="schedules-filter-form" style="margin-bottom:1rem">
                    <label>Buscar
                        <input name="search" type="search" placeholder="Nº programação, cliente ou pedido" value="${escapeHtml(filterParams.search || '')}" />
                    </label>
                    <label>Situação
                        <select name="status">
                            <option value="">Todas</option>
                            <option value="PLANNED" ${filterParams.status === 'PLANNED' ? 'selected' : ''}>Planejada (PLANNED)</option>
                            <option value="PREPARING" ${filterParams.status === 'PREPARING' ? 'selected' : ''}>Em Preparação (PREPARING)</option>
                            <option value="DISPATCHED" ${filterParams.status === 'DISPATCHED' ? 'selected' : ''}>Expedida (DISPATCHED)</option>
                            <option value="PARTIALLY_DELIVERED" ${filterParams.status === 'PARTIALLY_DELIVERED' ? 'selected' : ''}>Parcialmente Entregue</option>
                            <option value="DELIVERED" ${filterParams.status === 'DELIVERED' ? 'selected' : ''}>Entregue</option>
                            <option value="CANCELLED" ${filterParams.status === 'CANCELLED' ? 'selected' : ''}>Cancelada</option>
                        </select>
                    </label>
                    <label style="display:flex;align-items:center;gap:.5rem;cursor:pointer">
                        <input type="checkbox" name="lateOnly" value="true" ${filterParams.lateOnly === 'true' || filterParams.lateOnly === true ? 'checked' : ''} />
                        Apenas atrasados
                    </label>
                    <button class="primary-button" type="submit">Filtrar</button>
                </form>`;

            const tableHtml = rows.length ? `
                <table class="logistics-table">
                    <thead>
                        <tr>
                            <th>Nº Programação</th>
                            <th>Pedido / Cliente</th>
                            <th>Data Prevista</th>
                            <th>Destino</th>
                            <th>Responsável</th>
                            <th>Situação</th>
                            <th>Progresso Itens</th>
                            <th>Ação</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${rows.map(s => {
                            const orderId = value(s, 'orderId', 'order_id');
                            const plannedRaw = String(value(s, 'plannedDate', 'planned_date') || '');
                            const rowStatus = String(value(s, 'status') || '');
                            const isLate = ['PLANNED', 'PREPARING', 'DISPATCHED', 'PARTIALLY_DELIVERED'].includes(rowStatus) && plannedRaw.length >= 10 && plannedRaw.slice(0, 10) < civilToday();
                            const plannedMatch = plannedRaw.match(/^(\d{4})-(\d{2})-(\d{2})/);
                            const planned = plannedMatch ? `${plannedMatch[3]}/${plannedMatch[2]}/${plannedMatch[1]}` : '—';
                            const itemsList = value(s, 'items') || [];
                            const itemsSummary = itemsList.map(i => `${escapeHtml(value(i, 'productName', 'product_name'))}: ${Number(value(i, 'quantity')).toLocaleString('pt-BR')} ${escapeHtml(value(i, 'unit'))}`).join(', ');

                            return `
                            <tr class="${isLate ? 'row--late' : ''}">
                                <td><strong>${escapeHtml(value(s, 'scheduleNumber', 'schedule_number'))}</strong></td>
                                <td>
                                    <strong>${escapeHtml(value(s, 'orderNumber', 'order_number'))}</strong><br>
                                    <small>${escapeHtml(value(s, 'customerName', 'customer_name'))}</small>
                                </td>
                                <td>
                                    <strong>${planned}</strong>
                                    ${isLate ? '<span class="status-pill status-pill--late" style="margin-left:.3rem">ATRASADO</span>' : ''}
                                </td>
                                <td>${escapeHtml(value(s, 'destination') || '—')}</td>
                                <td>${escapeHtml(value(s, 'responsibleName', 'responsible_name') || 'Não atribuído')}</td>
                                <td><span class="status-pill">${escapeHtml(value(s, 'status'))}</span></td>
                                <td style="font-size:.85rem">${itemsSummary || '—'}</td>
                                <td>
                                    <a class="secondary-button" href="/Commercial?view=orders&orderId=${encodeURIComponent(orderId || '')}">Abrir pedido</a>
                                    <a class="secondary-button" href="/Logistics?orderId=${encodeURIComponent(orderId || '')}">Expedições</a>
                                    ${(value(s, 'allowedActions') || []).includes('fulfill') ? '' : `<small>${escapeHtml(value(s, 'blockReason') || '')}</small>`}
                                </td>
                            </tr>`;
                        }).join('')}
                    </tbody>
                </table>
                <nav class="pagination" aria-label="Paginação">
                    <button type="button" class="secondary-button" data-schedule-page="${currentPage - 1}" ${currentPage <= 1 ? 'disabled' : ''}>Anterior</button>
                    <span>Página ${currentPage} de ${Math.max(1, Math.ceil(total / pageSize))} · ${total} compromisso(s)</span>
                    <button type="button" class="secondary-button" data-schedule-page="${currentPage + 1}" ${currentPage * pageSize >= total ? 'disabled' : ''}>Próxima</button>
                </nav>
            ` : '<p class="empty-state">Nenhum compromisso operacional encontrado com os filtros selecionados.</p>';

            content.innerHTML = indHtml + filterToolbar + tableHtml;

            content.querySelector('#schedules-filter-form')?.addEventListener('submit', e => {
                e.preventDefault();
                const form = e.currentTarget;
                loadSchedules({
                    search: form.search.value.trim(),
                    status: form.status.value,
                    lateOnly: form.lateOnly.checked ? 'true' : ''
                });
            });

            content.querySelectorAll('[data-schedule-order]').forEach(b => {
                b.addEventListener('click', () => loadOrderDetail(b.dataset.scheduleOrder));
            });

            content.querySelectorAll('[data-schedule-page]').forEach(b => {
                b.addEventListener('click', () => {
                    const f = content.querySelector('#schedules-filter-form');
                    loadSchedules({
                        search: f?.search?.value?.trim() || '',
                        status: f?.status?.value || '',
                        lateOnly: f?.lateOnly?.checked ? 'true' : '',
                        page: b.dataset.schedulePage
                    });
                });
            });
        } catch (err) {
            renderErrorState(err, 'Carteira de Compromissos');
        }
    }

    function switchView(view) {
        activeView = view;
        document.querySelectorAll('.journey-nav button').forEach(x => {
            x.classList.toggle('active', x.dataset.view === view);
        });
        updateBreadcrumb(view);

        const filters = document.querySelector('#queue-filters');
        if (filters) filters.hidden = !['queue', 'picking', 'shipments'].includes(view);

        if (view === 'queue' || view === 'picking' || view === 'shipments') {
            loadQueue();
        } else if (view === 'schedules') {
            loadSchedules();
        } else if (view === 'trips') {
            loadTrips();
        } else if (view === 'returns') {
            loadReturns();
        } else {
            content.innerHTML = `<p class="empty-state">Use os indicadores e a rastreabilidade da expedição. A área “${view}” mostra somente vínculos persistidos; integrações ausentes permanecem pendentes.</p>`;
        }
    }

    async function refresh() {
        try {
            await loadIndicators();
            if (activeView === 'returns') {
                await loadReturns();
            } else if (activeView === 'schedules') {
                await loadSchedules();
            } else if (activeView === 'queue' || activeView === 'picking' || activeView === 'shipments') {
                await loadQueue();
            } else if (activeView === 'trips') {
                await loadTrips();
            }
        } catch (e) {
            renderErrorState(e, 'Atualização de Logística');
        }
    }

    function escapeHtml(value) {
        const span = document.createElement('span');
        span.textContent = String(value ?? '');
        return span.innerHTML;
    }

    document.querySelector('#queue-filters')?.addEventListener('submit', e => {
        e.preventDefault();
        e.currentTarget.elements.page.value = '1';
        loadQueue(e.currentTarget);
    });

    document.querySelector('#refresh-logistics')?.addEventListener('click', refresh);

    document.querySelectorAll('.journey-nav button').forEach(b => {
        if (b.dataset.view === 'schedules' && !hasPermission('commercial.read')) b.hidden = true;
        b.addEventListener('click', () => switchView(b.dataset.view));
    });

    // Iniciar na aba correspondente ou abrir pedido direto vindo do Comercial
    const urlParams = new URLSearchParams(window.location.search);
    const orderIdParam = urlParams.get('orderId');
    const tabParam = urlParams.get('tab');
    const initialView = tabParam || (orderIdParam ? 'queue' : 'returns');
    switchView(initialView);
    refresh().then(() => {
        if (orderIdParam) {
            const isValidGuid = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/.test(orderIdParam);
            if (isValidGuid) {
                loadOrderDetail(orderIdParam);
            } else {
                window.agro360Feedback?.toast('error', 'Identificador inválido', 'O pedido informado na URL não é um identificador válido.');
            }
        }
    });
})();
