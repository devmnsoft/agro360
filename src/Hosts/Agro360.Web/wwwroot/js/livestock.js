(() => {
    "use strict";
    const app = document.getElementById("livestock-app");
    if (!app) return;

    const apiBase = document.querySelector('meta[name="api-base"]')?.content?.replace(/\/$/, "") ?? "http://localhost:8081";
    const dialog = document.getElementById("livestock-dialog");
    const form = document.getElementById("livestock-form");
    const fields = document.getElementById("dialog-fields");
    const errorBox = document.getElementById("dialog-error");
    const content = document.getElementById("content");
    const toast = document.getElementById("livestock-toast");
    const helps = {
        overview: ["Visão consolidada do rebanho na data de referência, sem projetar o passado só com a situação atual.", "Propriedades, animais ou grupos já cadastrados.", "Atualize os indicadores e abra a origem de cada cartão nas demais abas.", "Nada é gravado nesta aba.", "Se um número parecer inconsistente, abra o relatório na data desejada."],
        animals: ["Cadastro e histórico do animal identificado.", "Propriedade, espécie, categoria e, se houver, grupo individual.", "Busque pelo brinco, abra o detalhe e registre identificação, origem e observações.", "Gera o animal, identificador e evento de cadastro.", "Troca de brinco preserva histórico; inativação não substitui venda, morte ou transferência."],
        groups: ["Grupos (indivíduos ou quantidade), lotes de manejo e instalações.", "Propriedade e tipo de controle.", "Cadastre curral/piquete separado do lote de produto. Grupo por quantidade não recebe animais identificados.", "Atualiza lotação e localização operacional.", "Conciliação coletiva→individual exige a mesma quantidade, sem criar cabeças artificiais."],
        movements: ["Entrada, transferência interna e saída justificada.", "Origem, data, responsável e motivo.", "Use transferência só entre propriedades do mesmo cliente. Venda comercial passa pela reserva.", "Altera localização atual de forma atômica; não gera saldo negativo.", "Corrija com ajuste ou estorno auditado, sem apagar a trilha."],
        handling: ["Ordens de manejo com população planejada congelada.", "Animais elegíveis, tipo, data, responsável.", "Rascunho → programado → liberado → em execução → concluído. Massa parcial mostra resultado por item.", "Cria tarefa operacional e apontamentos. Cancelar não apaga consumo já feito.", "Repita somente os itens que falharam."],
        weighings: ["Pesagem individual ou coletiva.", "Animal ou grupo, peso positivo e unidade.", "Coletiva não inventa peso por cabeça. Fora do limite pede justificativa.", "Atualiza o peso atual só na pesagem individual.", "Correção preserva o valor anterior e o responsável."],
        feeding: ["Plano não movimenta estoque; fornecimento consome o lote aprovado.", "Plano, depósito, produto e cabeças.", "Informe fornecido, devolvido e perdido. Devolução imprópria não volta ao uso livre.", "Gera consumo único no estoque e custo rastreável.", "Registre a devolução nesta aba, sem baixar de novo."],
        restrictions: ["Restrições operacionais e carências cadastradas.", "Evento sanitário ou restrição demonstrativa identificada.", "Alertas mostram origem, impedimento e providência. Permissão administrativa não as encerra sozinha.", "Bloqueia venda, manejo ou leite conforme a finalidade.", "Liberação exige critério e autoridade previstos."],
        commercial: ["Reserva, saída física e obrigação financeira são etapas distintas.", "Animal elegível, sem restrição incompatível.", "Reservar não vende; vender não quita. Revalidação ocorre na saída.", "Muda situação para reservado ou vendido e pode gerar recebível existente.", "Cancelar libera só a reserva vigente."],
        costs: ["Custos por animal, grupo e natureza (realizado, compromisso, estimativa).", "Lançamentos de aquisição, ração, manejo e materiais.", "Consulte a origem. Transferência interna não cria receita.", "Não duplica compra com consumo já apropriado.", "Ausência de peso não gera divisão por zero."],
        reports: ["Exporta o conjunto filtrado autorizado, não só a página.", "Filtros de período, propriedade e situação.", "CSV protege fórmula, aspas e separadores.", "Não grava dados.", "Se o arquivo vier vazio, os filtros não encontraram registros autorizados."]
    };
    let tab = new URLSearchParams(location.search).get("tab") || "overview";
    let farms = [];
    let toastTimer = 0;
    let currentAction = "animal";

    const session = () => { try { return JSON.parse(localStorage.getItem("agro360.session")); } catch { return null; } };
    const canWrite = () => {
        const current = session();
        const perms = (current?.permissions ?? []).map(value => String(value).toLowerCase().replace(/^agro360\./, "").replaceAll("_", "."));
        return current?.roles?.includes("SUPER_ADMIN") || perms.includes("livestock.write") || perms.includes("livestock.sell");
    };
    const escapeHtml = value => String(value ?? "").replace(/[&<>"']/g, ch => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[ch]));
    async function request(path, options = {}) {
        const headers = new Headers(options.headers ?? {});
        const token = session()?.accessToken ?? localStorage.getItem("agro360.accessToken");
        if (token) headers.set("Authorization", `Bearer ${token}`);
        if (options.body) headers.set("Content-Type", "application/json");
        const response = await fetch(`${apiBase}${path}`, { ...options, headers });
        if (response.status === 401) throw new Error("Sua sessão expirou. Entre novamente.");
        if (!response.ok) {
            const problem = await response.json().catch(() => ({}));
            throw new Error(Object.values(problem.errors ?? {}).flat().join(" ") || problem.detail || problem.title || `Falha HTTP ${response.status}.`);
        }
        if (response.status === 204) return null;
        const type = response.headers.get("content-type") || "";
        if (type.includes("csv")) return response.blob();
        return response.json();
    }
    function notify(message, isError = false) {
        clearTimeout(toastTimer);
        toast.textContent = message;
        toast.classList.toggle("error", isError);
        toast.hidden = false;
        toastTimer = window.setTimeout(() => { toast.hidden = true; }, 5000);
    }
    function setHelp() {
        const texts = helps[tab] || helps.overview;
        const labels = ["Para que serve", "O que precisa estar cadastrado", "Como realizar", "O que muda no sistema", "Como corrigir"];
        document.getElementById("tab-help").innerHTML = texts.map((text, index) => `<p><strong>${labels[index]}:</strong> ${text}</p>`).join("");
    }
    function farmId() { return document.getElementById("farm-filter").value || null; }
    function statusPill(value) {
        const danger = /SOLD|DEAD|BLOCKED|CANCELLED/.test(value || "");
        const warn = /RESERVED|ACTIVE_RESTRICTION|REVIEW/.test(value || "");
        return `<span class="status-pill${danger ? " danger" : warn ? " warn" : ""}">${escapeHtml(value ?? "—")}</span>`;
    }
    function table(headers, rows, empty) {
        if (!rows?.length) return `<p class="empty-state">${empty}</p>`;
        return `<div class="table-wrap"><table><thead><tr>${headers.map(h => `<th>${h}</th>`).join("")}</tr></thead><tbody>${rows.join("")}</tbody></table></div>`;
    }

    async function loadFarms() {
        const result = await request("/api/livestock/lookups/properties?pageSize=50");
        farms = result.items ?? [];
        const select = document.getElementById("farm-filter");
        const current = select.value;
        select.innerHTML = `<option value="">Todas as autorizadas</option>${farms.map(item => `<option value="${item.id}">${escapeHtml(item.label)}</option>`).join("")}`;
        if (current) select.value = current;
    }

    async function loadDashboard() {
        const data = await request("/api/livestock/dashboard");
        document.getElementById("kpi-date").textContent = data.referenceDate ? new Date(data.referenceDate).toLocaleDateString("pt-BR") : new Date().toLocaleDateString("pt-BR");
        document.getElementById("kpi-animals").textContent = data.activeAnimals ?? 0;
        document.getElementById("kpi-herds").textContent = data.activeHerds ?? 0;
        document.getElementById("kpi-restrictions").textContent = data.restrictedAnimals ?? data.inWithdrawal ?? 0;
        document.getElementById("kpi-reservations").textContent = data.commercialReservations ?? 0;
        document.getElementById("kpi-handlings").textContent = data.pendingHandlings ?? 0;
        return data;
    }

    async function render() {
        setHelp();
        content.innerHTML = `<p class="empty-state">Carregando…</p>`;
        document.querySelectorAll(".livestock-tabs button").forEach(button => button.classList.toggle("active", button.dataset.tab === tab));
        document.getElementById("primary-action").hidden = tab === "overview" || tab === "reports" || tab === "costs" || !canWrite();
        try {
            const data = await loadDashboard();
            if (tab === "overview") {
                content.innerHTML = `
                    <div class="preview-box">
                        <p>Rebanho na data de referência ${escapeHtml(document.getElementById("kpi-date").textContent)}. Entradas no mês: ${data.movementsIn ?? 0}. Saídas no mês: ${data.movementsOut ?? 0}.</p>
                        <p>Cada indicador abre os registros de origem nas abas correspondentes. Pesagens recentes: ${data.recentWeighings ?? 0}.</p>
                    </div>`;
                return;
            }
            if (tab === "animals") {
                const page = await request(`/api/v1/livestock/animals?pageSize=50${farmId() ? `&farmId=${farmId()}` : ""}${searchValue()}`);
                content.innerHTML = table(["Brinco", "Espécie", "Situação", "Peso", "Carência", "Ações"],
                    (page.items ?? []).map(item => `<tr>
                        <td><button type="button" data-open-animal="${item.id}">${escapeHtml(item.tag)}</button></td>
                        <td>${escapeHtml(item.species)} · ${escapeHtml(item.breed)}</td>
                        <td>${statusPill(item.status)}</td>
                        <td>${item.currentWeightKg ?? "—"} kg</td>
                        <td>${item.withdrawalUntil ?? "—"}</td>
                        <td><button type="button" data-open-animal="${item.id}">Detalhe</button></td>
                    </tr>`), "Nenhum animal autorizado neste filtro.");
                return;
            }
            if (tab === "groups") {
                const [herds, facilities, lots] = await Promise.all([
                    request("/api/livestock/herds"),
                    request("/api/livestock/facilities"),
                    request("/api/livestock/handling-lots")
                ]);
                content.innerHTML = `
                    <h3>Grupos</h3>${table(["Nome", "Controle", "Cabeças", "Situação"], (herds ?? []).map(item => `<tr><td>${escapeHtml(item.name)}</td><td>${escapeHtml(item.controlMode || item.control_mode || "INDIVIDUAL")}</td><td>${item.headCount ?? item.head_count ?? 0}</td><td>${statusPill(item.status)}</td></tr>`), "Nenhum grupo cadastrado.")}
                    <h3>Instalações</h3>${table(["Nome", "Tipo", "Capacidade", "Situação"], (facilities ?? []).map(item => `<tr><td>${escapeHtml(item.name)}</td><td>${escapeHtml(item.kind)}</td><td>${item.capacityHead ?? item.capacity_head ?? "—"}</td><td>${statusPill(item.status)}</td></tr>`), "Cadastre curral, piquete ou outra instalação.")}
                    <h3>Lotes de manejo</h3>${table(["Nome", "Finalidade", "Situação", "Cabeças"], (lots ?? []).map(item => `<tr><td>${escapeHtml(item.name)}</td><td>${escapeHtml(item.purpose)}</td><td>${statusPill(item.status)}</td><td>${item.headCount ?? 0}</td></tr>`), "Nenhum lote de manejo aberto.")}`;
                return;
            }
            if (tab === "movements") {
                const rows = await request(`/api/livestock/movements${farmId() ? `?farmId=${farmId()}` : ""}`);
                content.innerHTML = table(["Quando", "Tipo", "Sujeito", "Motivo", "Observação"],
                    (rows ?? []).map(item => `<tr><td>${escapeHtml(item.occurredOn || item.occurred_on)}</td><td>${escapeHtml(item.kind)}</td><td>${escapeHtml(item.subject)} (${escapeHtml(item.subjectKind || item.subject_kind)})</td><td>${escapeHtml(item.reasonCode || item.reason_code)}</td><td>${escapeHtml(item.notes || "")}</td></tr>`),
                    "Nenhuma movimentação no filtro.");
                return;
            }
            if (tab === "handling") {
                const rows = await request("/api/livestock/handling-orders");
                content.innerHTML = table(["Tipo", "Data", "Situação", "Planejados", "Atendidos", "Não atendidos", "Impedidos", "Ações"],
                    (rows ?? []).map(item => `<tr>
                        <td>${escapeHtml(item.handlingType || item.handling_type)}</td>
                        <td>${escapeHtml(item.plannedOn || item.planned_on)}</td>
                        <td>${statusPill(item.status)}</td>
                        <td>${item.plannedHeadCount ?? item.planned_head_count}</td>
                        <td>${item.attendedHeadCount ?? item.attended_head_count}</td>
                        <td>${item.notAttendedHeadCount ?? item.not_attended_head_count}</td>
                        <td>${item.blockedHeadCount ?? item.blocked_head_count}</td>
                        <td><button type="button" data-preview="${item.id}">Prévia</button></td>
                    </tr>`), "Nenhuma ordem de manejo.");
                return;
            }
            if (tab === "weighings") {
                const rows = await request("/api/livestock/weighings");
                content.innerHTML = table(["Quando", "Escopo", "Peso", "Unidade", "Origem", "Revisão"],
                    (rows ?? []).map(item => `<tr><td>${escapeHtml(item.weighedAt || item.weighed_at)}</td><td>${escapeHtml(item.scope)}</td><td>${item.weightKg ?? item.weight_kg}</td><td>${escapeHtml(item.unit)}</td><td>${escapeHtml(item.source)}</td><td>${item.reviewRequired || item.review_required ? "Sim" : "Não"}</td></tr>`),
                    "Nenhuma pesagem registrada.");
                return;
            }
            if (tab === "feeding") {
                const [plans, feedings] = await Promise.all([request("/api/livestock/nutrition-plans"), request("/api/livestock/feedings")]);
                content.innerHTML = `
                    <h3>Planos (não movimentam estoque)</h3>${table(["Nome", "Início", "Custo diário"], (plans ?? []).map(item => `<tr><td>${escapeHtml(item.name)}</td><td>${escapeHtml(item.starts_on || item.startsOn)}</td><td>${item.daily_cost ?? item.dailyCost ?? "—"}</td></tr>`), "Nenhum plano alimentar.")}
                    <h3>Fornecimentos</h3>${table(["Data", "Cabeças", "Fornecido", "Devolvido", "Consumo", "Custo"], (feedings ?? []).map(item => `<tr><td>${escapeHtml(item.supplied_on || item.suppliedOn)}</td><td>${item.head_count ?? item.headCount}</td><td>${item.supplied_quantity ?? item.suppliedQuantity ?? "—"}</td><td>${item.returned_quantity ?? item.returnedQuantity ?? 0}</td><td>${item.consumed_quantity ?? item.consumedQuantity ?? "—"}</td><td>${item.total_cost ?? item.totalCost}</td></tr>`), "Nenhum fornecimento.")}`;
                return;
            }
            if (tab === "restrictions") {
                const rows = await request("/api/livestock/restrictions?onlyActive=false");
                content.innerHTML = table(["Animal", "Finalidade", "Motivo", "Início", "Previsão", "Situação"],
                    (rows ?? []).map(item => `<tr><td>${escapeHtml(item.tag || "Grupo")}</td><td>${escapeHtml(item.purpose)}</td><td>${escapeHtml(item.reason)}${item.demoOnly || item.demo_only ? " (demonstrativo)" : ""}</td><td>${escapeHtml(item.startedOn || item.started_on)}</td><td>${escapeHtml(item.expectedUntil || item.expected_until || "—")}</td><td>${statusPill(item.status)}</td></tr>`),
                    "Nenhuma restrição cadastrada.");
                return;
            }
            if (tab === "commercial") {
                const eligible = farmId() ? await request(`/api/livestock/commercial/eligible?farmId=${farmId()}`) : [];
                content.innerHTML = `
                    <p>Reserva ≠ venda ≠ pagamento. A saída física revalida restrições e carência.</p>
                    ${table(["Animal", "Peso", "Restrito", "Situação"], (eligible ?? []).map(item => `<tr><td>${escapeHtml(item.tag)}</td><td>${item.currentWeightKg ?? item.current_weight_kg ?? "—"}</td><td>${item.restricted ? "Sim" : "Não"}</td><td>${statusPill(item.status)}</td></tr>`), farmId() ? "Nenhum animal elegível." : "Selecione uma propriedade para listar elegíveis.")}`;
                return;
            }
            if (tab === "costs") {
                const rows = await request("/api/livestock/costs");
                content.innerHTML = table(["Categoria", "Natureza", "Valor", "Lançamentos", "Base"],
                    (rows ?? []).map(item => `<tr><td>${escapeHtml(item.category)}</td><td>${escapeHtml(item.nature)}</td><td>${item.amount}</td><td>${item.entries}</td><td>${escapeHtml(item.basis)}</td></tr>`),
                    "Sem custos apropriados no filtro. Margem só aparece com receita e custo suficientes.");
                return;
            }
            if (tab === "reports") {
                const asOf = new Date().toISOString().slice(0, 10);
                const rows = await request(`/api/livestock/reports/herd?asOf=${asOf}${farmId() ? `&farmId=${farmId()}` : ""}`);
                content.innerHTML = `<p>Relatório na data ${asOf}. Não usa apenas a situação atual para descrever o passado.</p>` +
                    table(["Propriedade", "Categoria", "Cabeças na data"], (rows ?? []).map(item => `<tr><td>${escapeHtml(item.farmName || item.farm_name)}</td><td>${escapeHtml(item.category)}</td><td>${item.headCount ?? item.head_count}</td></tr>`), "Sem cabeças na data informada.");
            }
        } catch (error) {
            content.innerHTML = `<p class="empty-state">${escapeHtml(error.message)}</p>`;
            notify(error.message, true);
        }
    }

    function searchValue() {
        const value = document.getElementById("search").value.trim();
        return value ? `&search=${encodeURIComponent(value)}` : "";
    }

    function openDialog(title, html, action) {
        currentAction = action;
        document.getElementById("dialog-title").textContent = title;
        fields.innerHTML = html;
        errorBox.textContent = "";
        dialog.showModal();
        bindLookups();
    }

    function bindLookups() {
        form.querySelectorAll(".lookup").forEach(input => {
            let timer;
            input.addEventListener("input", () => {
                form.elements[input.dataset.target].value = "";
                clearTimeout(timer);
                if (input.value.length < 2) return;
                timer = setTimeout(async () => {
                    try {
                        const data = await request(`/api/livestock/lookups/${input.dataset.lookup}?search=${encodeURIComponent(input.value)}`);
                        input.parentElement.querySelector(".lookup-results")?.remove();
                        const box = document.createElement("div");
                        box.className = "lookup-results";
                        (data.items ?? []).forEach(item => {
                            const option = document.createElement("button");
                            option.type = "button";
                            option.textContent = `${item.label} — ${item.description}`;
                            option.addEventListener("click", () => {
                                input.value = item.label;
                                form.elements[input.dataset.target].value = item.id;
                                box.remove();
                            });
                            box.append(option);
                        });
                        input.parentElement.append(box);
                    } catch (error) { errorBox.textContent = error.message; }
                }, 250);
            });
        });
    }

    document.getElementById("primary-action").addEventListener("click", () => {
        const farmField = `<label>Propriedade<input class="lookup" data-lookup="properties" data-target="farmId" required minlength="2" placeholder="Busque pelo nome" /><input type="hidden" name="farmId" /></label>`;
        const animalField = `<label>Animal<input class="lookup" data-lookup="animals" data-target="animalId" required minlength="2" placeholder="Brinco" /><input type="hidden" name="animalId" /></label>`;
        if (tab === "animals") {
            openDialog("Cadastrar animal", `${farmField}
                <label>Brinco / identificador<input name="tag" required maxlength="80" data-help="Único no cliente. A troca posterior preserva o histórico." /></label>
                <label>Espécie<input name="species" required maxlength="60" value="BOVINE" /></label>
                <label>Raça<input name="breed" required maxlength="80" /></label>
                <label>Sexo<select name="sex" required><option value="M">Macho</option><option value="F">Fêmea</option></select></label>
                <label>Nascimento<input name="birthDate" type="date" required /></label>
                <label>Data estimada?<select name="birthDateEstimated"><option value="false">Não</option><option value="true">Sim, estimada</option></select></label>
                <label>Origem<select name="originType"><option value="PURCHASE">Compra</option><option value="BIRTH">Nascimento</option><option value="OTHER">Outra</option></select></label>
                <label class="wide">Observações<textarea name="notes" maxlength="2000"></textarea></label>`, "animal");
            return;
        }
        if (tab === "groups") {
            openDialog("Nova instalação", `${farmField}
                <label>Nome<input name="name" required maxlength="120" /></label>
                <label>Tipo<select name="kind" required><option value="CORRAL">Curral</option><option value="PADDOCK">Piquete</option><option value="BARN">Curral coberto</option><option value="PEN">Baia</option><option value="OTHER">Outra</option></select></label>
                <label>Capacidade (cabeças)<input name="capacityHead" type="number" min="0" step="1" /></label>
                <label>Situação<select name="status"><option>AVAILABLE</option><option>IN_USE</option></select></label>`, "facility");
            return;
        }
        if (tab === "movements") {
            openDialog("Movimentar rebanho", `${animalField}
                <label>Tipo<select name="kind" required><option value="ENTRY">Entrada</option><option value="TRANSFER">Transferência interna</option><option value="LOCATION_CHANGE">Mudança de instalação</option><option value="EXIT">Saída (morte/descarte)</option></select></label>
                <label>Motivo<select name="reasonCode" required><option value="PURCHASE">Compra</option><option value="BIRTH">Nascimento</option><option value="INTERNAL_TRANSFER">Transferência interna</option><option value="DEATH">Morte</option><option value="DISCARD">Descarte</option></select></label>
                <label>Data<input name="occurredOn" type="date" required /></label>
                <label>Responsável<input class="lookup" data-lookup="people" data-target="responsibleId" required minlength="2" /><input type="hidden" name="responsibleId" /></label>
                <label>Destino (propriedade)<input class="lookup" data-lookup="properties" data-target="toFarmId" minlength="2" /><input type="hidden" name="toFarmId" /></label>
                <label class="wide">Observações<textarea name="notes"></textarea></label>`, "movement");
            return;
        }
        if (tab === "handling") {
            openDialog("Nova ordem de manejo", `${farmField}
                <label>Tipo<select name="handlingType" required><option value="SORTING">Apartação</option><option value="WEIGHING">Pesagem</option><option value="VACCINATION">Vacinação cadastrada</option></select></label>
                <label>Data prevista<input name="plannedOn" type="date" required /></label>
                <label>Prioridade<select name="priority"><option>MEDIUM</option><option>HIGH</option><option>LOW</option></select></label>
                <label>Responsável<input class="lookup" data-lookup="people" data-target="responsibleId" required minlength="2" /><input type="hidden" name="responsibleId" /></label>
                <label>Animal inicial<input class="lookup" data-lookup="animals" data-target="animalId" required minlength="2" /><input type="hidden" name="animalId" /></label>
                <label>Processamento<select name="massAtomic"><option value="false">Aceita resultado parcial</option><option value="true">Atômico (tudo ou nada)</option></select></label>
                <label class="wide">Instruções<textarea name="instructions"></textarea></label>`, "handling");
            return;
        }
        if (tab === "weighings") {
            openDialog("Registrar pesagem", `${farmField}${animalField}
                <label>Escopo<select name="scope"><option value="INDIVIDUAL">Individual</option><option value="COLLECTIVE">Coletiva</option></select></label>
                <label>Peso<input name="weight" type="number" min="0.001" step="0.001" required /></label>
                <label>Unidade<select name="unit"><option value="kg">kg</option><option value="arroba">arroba</option></select></label>
                <label>Fator de conversão<input name="conversionFactor" type="number" min="0" step="0.0001" data-help="Obrigatório se a unidade não for kg. Arroba = 15 kg." /></label>
                <label>Data e hora<input name="weighedAt" type="datetime-local" required /></label>
                <label class="wide">Observações / justificativa de limite<textarea name="notes"></textarea></label>`, "weighing");
            return;
        }
        if (tab === "commercial") {
            openDialog("Reservar para venda", `${farmField}${animalField}
                <label>Data da reserva<input name="reservedOn" type="date" required /></label>
                <label>Comprador<input name="buyerName" maxlength="160" /></label>
                <label class="wide">Observações<textarea name="notes">Reserva não representa venda concluída nem pagamento recebido.</textarea></label>`, "reserve");
            return;
        }
        notify("Esta aba não cria registro por este botão. Use a operação específica listada.", true);
    });

    form.addEventListener("submit", async event => {
        event.preventDefault();
        errorBox.textContent = "";
        const raw = Object.fromEntries(new FormData(form));
        const button = form.querySelector('[type="submit"]');
        button.disabled = true;
        try {
            if (currentAction === "animal") {
                await request("/api/v1/livestock/animals", { method: "POST", body: JSON.stringify({
                    farmId: raw.farmId, tag: raw.tag, species: raw.species, breed: raw.breed, sex: raw.sex,
                    birthDate: raw.birthDate, birthDateEstimated: raw.birthDateEstimated === "true",
                    originType: raw.originType, notes: raw.notes || null
                }) });
            } else if (currentAction === "facility") {
                await request("/api/livestock/facilities", { method: "POST", body: JSON.stringify({
                    farmId: raw.farmId, name: raw.name, kind: raw.kind, capacityHead: raw.capacityHead ? Number(raw.capacityHead) : null,
                    status: raw.status, notes: null, paddockId: null
                }) });
            } else if (currentAction === "movement") {
                await request("/api/livestock/movements", { method: "POST", body: JSON.stringify({
                    animalId: raw.animalId, kind: raw.kind, reasonCode: raw.reasonCode, occurredOn: raw.occurredOn,
                    responsibleId: raw.responsibleId, toFarmId: raw.toFarmId || null, notes: raw.notes || null
                }) });
            } else if (currentAction === "handling") {
                await request("/api/livestock/handling-orders", { method: "POST", body: JSON.stringify({
                    farmId: raw.farmId, handlingType: raw.handlingType, plannedOn: raw.plannedOn, responsibleId: raw.responsibleId,
                    priority: raw.priority, massAtomic: raw.massAtomic === "true", animalIds: [raw.animalId], instructions: raw.instructions || null
                }) });
            } else if (currentAction === "weighing") {
                await request("/api/livestock/weighings", { method: "POST", body: JSON.stringify({
                    scope: raw.scope, farmId: raw.farmId, animalId: raw.scope === "INDIVIDUAL" ? raw.animalId : null,
                    weight: Number(raw.weight), unit: raw.unit, conversionFactor: raw.conversionFactor ? Number(raw.conversionFactor) : (raw.unit === "arroba" ? 15 : null),
                    weighedAt: new Date(raw.weighedAt).toISOString(), source: "MANUAL", notes: raw.notes || null, reviewJustification: raw.notes || null
                }) });
            } else if (currentAction === "reserve") {
                await request("/api/livestock/commercial/reservations", { method: "POST", body: JSON.stringify({
                    farmId: raw.farmId, animalId: raw.animalId, quantity: 1, reservedOn: raw.reservedOn, buyerName: raw.buyerName || null, notes: raw.notes || null
                }) });
            }
            dialog.close();
            notify("Operação registrada.");
            await render();
        } catch (error) {
            errorBox.textContent = error.message;
            notify(error.message, true);
        } finally {
            button.disabled = false;
        }
    });

    content.addEventListener("click", async event => {
        const animal = event.target.closest("[data-open-animal]");
        if (animal) {
            try {
                const detail = await request(`/api/livestock/animals/${animal.dataset.openAnimal}/detail`);
                content.innerHTML = `<article class="preview-box"><h3>${escapeHtml(detail.animal.tag)}</h3>
                    <p>Situação ${escapeHtml(detail.animal.status)}. Origem ${escapeHtml(detail.animal.originType || "não informada")}. Nascimento ${detail.animal.birthDateEstimated ? "estimado" : "conhecido"} em ${escapeHtml(detail.animal.birthDate)}.</p>
                    <p>Propriedade ${escapeHtml(detail.animal.farmName)}. Grupo ${escapeHtml(detail.animal.herdName || "—")} (${escapeHtml(detail.animal.herdControlMode || "—")}).</p>
                    <h4>Linha do tempo</h4>${(detail.timeline ?? []).map(item => `<p>${escapeHtml(item.occurredOn || item.occurred_on)} · ${escapeHtml(item.eventType || item.event_type)}</p>`).join("") || "<p>Sem eventos.</p>"}
                    <h4>Pesagens</h4>${(detail.weighings ?? []).map(item => `<p>${escapeHtml(item.weighedAt || item.weighed_at)} · ${item.weightKg || item.weight_kg} kg</p>`).join("") || "<p>Sem pesagens comparáveis.</p>"}
                    <button type="button" id="back-list">Voltar à lista</button></article>`;
                document.getElementById("back-list").addEventListener("click", render);
            } catch (error) { notify(error.message, true); }
            return;
        }
        const preview = event.target.closest("[data-preview]");
        if (preview) {
            try {
                const rows = await request(`/api/livestock/handling-orders/${preview.dataset.preview}/preview`);
                notify(`Prévia: ${(rows ?? []).length} animal(is) planejados. Confira elegibilidade antes de executar.`);
                content.insertAdjacentHTML("afterbegin", `<div class="preview-box">${(rows ?? []).map(item => `<p>${escapeHtml(item.tag)} · ${escapeHtml(item.eligibility)} · ${escapeHtml(item.outcome)}</p>`).join("")}</div>`);
            } catch (error) { notify(error.message, true); }
        }
    });

    document.getElementById("export").addEventListener("click", async () => {
        const kind = tab === "weighings" ? "weighings" : tab === "movements" ? "movements" : tab === "restrictions" ? "restrictions" : "animals";
        try {
            const blob = await request(`/api/livestock/reports/export?kind=${kind}${farmId() ? `&farmId=${farmId()}` : ""}`);
            const url = URL.createObjectURL(blob);
            const link = document.createElement("a");
            link.href = url;
            link.download = `pecuaria-${kind}.csv`;
            link.click();
            URL.revokeObjectURL(url);
        } catch (error) { notify(error.message, true); }
    });

    document.querySelectorAll(".livestock-tabs button").forEach(button => button.addEventListener("click", () => {
        tab = button.dataset.tab;
        history.replaceState(null, "", `/livestock?tab=${tab}`);
        render();
    }));
    document.getElementById("refresh").addEventListener("click", render);
    document.getElementById("farm-filter").addEventListener("change", render);
    document.getElementById("search").addEventListener("change", render);
    document.querySelectorAll("[data-close]").forEach(button => button.addEventListener("click", () => dialog.close()));
    document.querySelectorAll(".livestock-tabs button").forEach(button => {
        if (button.dataset.tab === tab) button.classList.add("active");
    });

    loadFarms().then(render).catch(error => notify(error.message, true));
})();
