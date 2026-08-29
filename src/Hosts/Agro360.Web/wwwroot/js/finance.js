(() => {
  const api = document.querySelector('meta[name="api-base"]')?.content || '';
  const content = document.querySelector('#finance-content');
  const title = document.querySelector('#finance-title');
  const subtitle = document.querySelector('#finance-subtitle');
  const tabs = [...document.querySelectorAll('[data-finance-tab]')];
  const pageSize = 20;
  let resource = 'dashboard', rows = [], page = 1;
  const labels = { dashboard:'Visão executiva', payables:'Contas a pagar', receivables:'Contas a receber', 'chart-of-accounts':'Plano de contas', 'cost-centers':'Centros de custo', budgets:'Orçamentos', 'cash-flow':'Fluxo de caixa', dre:'DRE gerencial', profitability:'Rentabilidade', audit:'Auditoria financeira' };
  const endpoints = {dashboard:'dashboard',payables:'payables',receivables:'receivables','chart-of-accounts':'chart-of-accounts','cost-centers':'cost-centers',budgets:'budgets','cash-flow':'cash-flow',dre:'results',profitability:'results'};
  const token = () => localStorage.getItem('agro360.accessToken');
  const money = value => new Intl.NumberFormat('pt-BR',{style:'currency',currency:'BRL'}).format(Number(value || 0));
  const safe = value => String(value ?? '—').replace(/[&<>'"]/g, char => ({'&':'&amp;','<':'&lt;','>':'&gt;',"'":'&#39;','"':'&quot;'}[char]));
  async function request(path) {
    const query = new URLSearchParams(); const from = document.querySelector('#finance-from').value; const to = document.querySelector('#finance-to').value;
    if(from) query.set('from',from); if(to) query.set('to',to);
    const response = await fetch(`${api}/api/finance/${path}${query.size ? `?${query}` : ''}`, {headers:{Authorization:`Bearer ${token() || ''}`}});
    if(!response.ok) throw new Error(response.status === 403 ? 'Seu perfil não possui permissão para consultar estes valores.' : 'Não foi possível consultar o financeiro agora.');
    return response.json();
  }
  function metric(data,key,...fallbacks){ const value=[key,...fallbacks].map(k=>data?.[k] ?? data?.[k[0].toUpperCase()+k.slice(1)]).find(v=>v!==undefined); const node=document.querySelector(`[data-metric="${key}"]`); if(node) node.textContent=money(value); }
  async function dashboard(){ const data=await request('dashboard'); metric(data,'expectedRevenue','receivableMonth');metric(data,'actualRevenue','monthlyRevenue');metric(data,'expectedExpense','payableMonth');metric(data,'actualExpense','monthlyExpense');metric(data,'expectedBalance');metric(data,'actualBalance');metric(data,'overduePayables');metric(data,'overdueReceivables'); return [data]; }
  function render(){ const search=document.querySelector('#finance-search').value.trim().toLowerCase(); const filtered=rows.filter(x=>!search||Object.values(x).some(v=>String(v??'').toLowerCase().includes(search))); const slice=filtered.slice((page-1)*pageSize,page*pageSize); document.querySelector('#finance-page').textContent=`Página ${page}`;
    if(!slice.length){content.innerHTML='<div class="finance-empty"><h3>Nenhum dado financeiro neste recorte</h3><p>Ajuste período e filtros. Lançamentos não classificados continuam visíveis nos relatórios.</p></div>';return;}
    const keys=Object.keys(slice[0]).filter(k=>!/(tenant|createdBy|updatedBy)/i.test(k)).slice(0,8); content.innerHTML=`<table class="finance-table"><thead><tr>${keys.map(k=>`<th>${safe(k.replace(/([A-Z_])/g,' $1'))}</th>`).join('')}</tr></thead><tbody>${slice.map(row=>`<tr>${keys.map(k=>`<td>${/amount|balance|revenue|cost|expense|margin/i.test(k)?money(row[k]):safe(row[k])}</td>`).join('')}</tr>`).join('')}</tbody></table>`;
  }
  async function load(next=resource){ resource=next; page=1; tabs.forEach(x=>x.classList.toggle('active',x.dataset.financeTab===resource)); title.textContent=labels[resource]; subtitle.textContent=resource==='dashboard'?'Dados realizados e previstos, sem conciliação automática.':'Consulta real, isolada pelo tenant e protegida por permissão.'; content.innerHTML='<div class="finance-loading"><span></span><p>Consultando lançamentos reais…</p></div>';
    try { if(resource==='dashboard') rows=await dashboard(); else if(endpoints[resource]) { const data=await request(endpoints[resource]); rows=Array.isArray(data)?data:[data]; } else { rows=[]; } render(); } catch(error){content.innerHTML=`<div class="finance-error"><h3>Consulta indisponível</h3><p>${safe(error.message)}</p></div>`;}
  }
  tabs.forEach(tab=>tab.addEventListener('click',()=>load(tab.dataset.financeTab))); document.querySelector('#finance-refresh').addEventListener('click',()=>load()); document.querySelector('#finance-search').addEventListener('input',()=>{page=1;render()}); document.querySelectorAll('#finance-from,#finance-to').forEach(x=>x.addEventListener('change',()=>load()));
  document.querySelector('#finance-prev').addEventListener('click',()=>{if(page>1){page--;render()}});document.querySelector('#finance-next').addEventListener('click',()=>{if(page*pageSize<rows.length){page++;render()}});
  document.querySelector('#finance-export').addEventListener('click',()=>{if(!rows.length)return;const keys=Object.keys(rows[0]);const csv=[keys.join(';'),...rows.map(r=>keys.map(k=>`"${String(r[k]??'').replaceAll('"','""')}"`).join(';'))].join('\n');const blob=new Blob(['\ufeff'+csv],{type:'text/csv;charset=utf-8'});const a=document.createElement('a');a.href=URL.createObjectURL(blob);a.download=`agro360-${resource}.csv`;a.click();URL.revokeObjectURL(a.href)});
  load();
})();
