(()=>{'use strict';
const culture=()=>localStorage.getItem('agro360.culture')||document.documentElement.lang||'pt-BR';
const copy={
'pt-BR':{required:'Este campo é obrigatório.',email:'Informe um e-mail válido.',invalid:'Revise o valor informado.',summary:'Revise os campos destacados antes de continuar.',help:'Como usar esta tela'},
'en-US':{required:'This field is required.',email:'Enter a valid email.',invalid:'Review the entered value.',summary:'Review the highlighted fields before continuing.',help:'How to use this screen'},
'es-ES':{required:'Este campo es obligatorio.',email:'Ingrese un correo válido.',invalid:'Revise el valor ingresado.',summary:'Revise los campos destacados antes de continuar.',help:'Cómo usar esta pantalla'},
'fr-FR':{required:'Ce champ est obligatoire.',email:'Saisissez une adresse e-mail valide.',invalid:'Vérifiez la valeur saisie.',summary:'Vérifiez les champs mis en évidence avant de continuer.',help:"Comment utiliser cet écran"}};
const helps={
default:'Use esta tela para consultar e manter os dados autorizados do tenant ativo. Preencha os campos marcados como obrigatórios e use os seletores para localizar cadastros, sem digitar IDs técnicos. As ações disponíveis dependem do seu perfil e regras do módulo. Antes de salvar, revise os dados; cancelamentos e ações críticas pedem confirmação. Depois da conclusão, o registro é auditado e os indicadores relacionados são atualizados.',
Agriculture:'Cadastre propriedades, produtores, talhões e safras do tenant ativo. Use os filtros para localizar registros; campos marcados são obrigatórios e alterações dependem da sua permissão.',
livestock:'Gerencie rebanho, grupos, manejos, pesagens e restrições do cliente ativo. Use os seletores por nome; GUID não é informado. Ações críticas pedem confirmação e a trilha não é apagada.',
fleet:'Gerencie equipamentos, leituras, preventiva, OS, peças e abastecimentos. Disponibilidade e bloqueios são explicados na tela; ações críticas pedem confirmação e preservam histórico.',
Finance:'Consulte títulos e fluxo financeiro do tenant. Valores usam a cultura selecionada; baixar, cancelar ou alterar lançamentos exige permissão e confirmação.',
Fiscal:'Emita e acompanhe documentos fiscais vinculados à operação real. Confira destinatário, impostos e totais antes de autorizar; rejeições não alteram estoque ou financeiro.',
Production:'Planeje ordens e aponte produção e qualidade. Selecione cadastros do tenant; encerrar uma ordem impede novos apontamentos e exige confirmação.',
commercial:'Crie e acompanhe propostas, pedidos e programação de entrega do tenant ativo. Programar entrega não reserva estoque; a reserva só ocorre na separação confirmada. Mudar data reavalia o saldo disponível por item e cancelamento exige motivo auditável. As ações disponíveis dependem do seu perfil e do estado do registro.',
logistics:'Gerencie compromissos de entrega, separação, conferência e expedições vinculadas aos pedidos autorizados. A separação reserva o saldo sem efetuar a saída física; a expedição exige conferência concluída e confirmação explícita. Não é permitido expedir compromissos cancelados ou sem saldo.',
Logistics:'Gerencie compromissos de entrega, separação, conferência e expedições vinculadas aos pedidos autorizados. A separação reserva o saldo sem efetuar a saída física; a expedição exige conferência concluída e confirmação explícita. Não é permitido expedir compromissos cancelados ou sem saldo.',
Reports:'Escolha relatório, período e filtros disponíveis. A exportação usa os dados autorizados no momento da solicitação e arquivos vazios não são simulados.'};
function enhanceHelp(){if(document.querySelector('.screen-help,.contextual-help'))return;const main=document.querySelector('main .page-shell,main > section,main > div:not(.topbar)');if(!main)return;const raw=location.pathname.split('/').filter(Boolean)[0]||'default',key=Object.keys(helps).find(k=>k.toLowerCase()===raw.toLowerCase())||'default',details=document.createElement('details');details.className='screen-help';const text=helps[key]||helps.default;details.innerHTML=`<summary>${(copy[culture()]||copy['pt-BR']).help}</summary><p>${text}</p>`;main.prepend(details)}
function errorFor(input){const t=copy[culture()]||copy['pt-BR'];return input.validity.valueMissing?t.required:input.validity.typeMismatch&&input.type==='email'?t.email:t.invalid}
let idSequence=0;
function uniqueValidationId(input){
const base=(input.id||input.name||'field_'+(++idSequence)).replace(/[^a-zA-Z0-9_-]/g,'_');
let candidate=base+'-validation';
let count=1;
while(document.getElementById(candidate)&&document.getElementById(candidate)!==input.nextElementSibling){
candidate=`${base}_${count++}-validation`;
}
return candidate;
}
function enhanceField(input, form){
if(!input||input.dataset.fieldEnhanced==='true')return;
input.dataset.fieldEnhanced='true';
const label=input.closest('label')||document.querySelector(`label[for="${CSS.escape(input.id||'')}"]`);
if(input.required&&label&&!label.querySelector('.required-mark'))label.insertAdjacentHTML('afterbegin','<span class="required-mark" aria-label="obrigatório">*</span> ');
if(input.dataset.help&&label&&!label.querySelector('.field-help')){
const help=document.createElement('button');
help.type='button';
help.className='field-help';
help.setAttribute('aria-label',`Ajuda sobre ${input.name||'este campo'}`);
help.dataset.tooltip=input.dataset.help;
help.textContent='?';
label.insertBefore(help,label.firstChild?.nextSibling||null);
}
let msg=input.nextElementSibling;
if(!msg||!msg.classList.contains('field-validation')){
msg=document.createElement('span');
msg.className='field-validation';
msg.id=uniqueValidationId(input);
input.insertAdjacentElement('afterend',msg);
}
const existingDescribedBy=(input.getAttribute('aria-describedby')||'').split(/\s+/).filter(Boolean);
if(!existingDescribedBy.includes(msg.id)){
existingDescribedBy.push(msg.id);
input.setAttribute('aria-describedby',existingDescribedBy.join(' '));
}
input.addEventListener('invalid',()=>{msg.textContent=errorFor(input)});
input.addEventListener('input',()=>{
if(input.validity.valid)msg.textContent='';
if(form){
const summary=form.querySelector('.form-validation-summary');
const bad=[...form.elements].filter(x=>x.willValidate&&!x.validity.valid);
if(summary&&!bad.length){
summary.hidden=true;
summary.textContent='';
}
}
});
}
function enhanceFormHandlers(form){
if(!form||form.dataset.formEnhanced==='true')return;
form.dataset.formEnhanced='true';
form.noValidate=false;
let summary=form.querySelector('.form-validation-summary');
if(!summary){
summary=document.createElement('div');
summary.className='form-validation-summary';
summary.role='alert';
summary.hidden=true;
form.prepend(summary);
}
form.addEventListener('submit',()=>{
const bad=[...form.elements].filter(x=>x.willValidate&&!x.validity.valid);
summary.hidden=!bad.length;
summary.textContent=bad.length?(copy[culture()]||copy['pt-BR']).summary:'';
if(!bad.length){form.setAttribute('aria-busy','true');}else{form.removeAttribute('aria-busy');}
});
form.addEventListener('reset',()=>{
summary.hidden=true;
summary.textContent='';
form.removeAttribute('aria-busy');
form.querySelectorAll('.field-validation').forEach(m=>{m.textContent='';});
});
}
function enhanceForm(form){
if(!form)return;
enhanceFormHandlers(form);
form.querySelectorAll('input,select,textarea').forEach(input=>enhanceField(input,form));
}
function clearBusy(form){
if(!form)return;
form.removeAttribute('aria-busy');
const btn=form.querySelector('button[type=submit],.primary-button');
if(btn)btn.disabled=false;
}
const dialog=document.querySelector('#action-confirmation'),reason=document.querySelector('#confirmation-reason'),reasonField=document.querySelector('#confirmation-reason-field'),reasonError=document.querySelector('#confirmation-reason-error');let pending=null;
document.addEventListener('click',event=>{const trigger=event.target.closest('[data-confirm-action]');if(!trigger||!dialog)return;if(trigger.dataset.confirmed==='true'){delete trigger.dataset.confirmed;return}event.preventDefault();pending=trigger;document.querySelector('#confirmation-title').textContent=trigger.dataset.confirmTitle||'Confirme a operação';document.querySelector('#confirmation-consequence').textContent=trigger.dataset.confirmConsequence||'';reasonField.hidden=trigger.dataset.requireReason!=='true';reason.value='';reasonError.textContent='';dialog.showModal()});
document.querySelector('#confirmation-form')?.addEventListener('submit',event=>{if(event.submitter?.value!=='confirm'){pending=null;return}if(!reasonField.hidden&&reason.value.trim().length<3){event.preventDefault();reasonError.textContent='Informe uma justificativa com pelo menos 3 caracteres.';return}const target=pending;pending=null;dialog.close();if(!target)return;if(!reasonField.hidden){const form=target.closest('form');const field=form?.querySelector('[name="reason"],[name="justification"]');if(field)field.value=reason.value.trim()}target.dataset.confirmed='true';target.click()});
document.querySelectorAll('form:not(#confirmation-form)').forEach(enhanceForm);
enhanceHelp();
window.agro360Forms={enhanceForm,enhanceField,enhanceAll:()=>document.querySelectorAll('form:not(#confirmation-form)').forEach(enhanceForm),clearBusy,enhanceHelp};
})();
