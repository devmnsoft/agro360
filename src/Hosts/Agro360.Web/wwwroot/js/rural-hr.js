(() => {
    'use strict';

    const root = document.querySelector('.rural-hr-shell');
    if (!root) return;

    const api = root.dataset.api;
    const content = document.querySelector('#hr-content');
    const feedback = document.querySelector('#hr-feedback');
    const dialog = document.querySelector('#hr-editor');
    const form = document.querySelector('#hr-form');

    const routes = {
        'Pessoas': 'people',
        'Equipes': 'teams',
        'Jornada': 'time-entries',
        'Alocações': 'allocations',
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

    let route = 'dashboard';

    const request = async (path, options = {}) => {
        const token = localStorage.getItem('agro360.accessToken');
        const response = await fetch(`${api}/${path}`, {
            ...options,
            headers: {
                'Content-Type': 'application/json',
                Authorization: `Bearer ${token}`,
                ...options.headers
            }
        });
        if (!response.ok) {
            throw new Error((await response.text()) || 'Não foi possível concluir a operação.');
        }
        return response.status === 204 ? null : response.json();
    };

    const renderEmpty = (message) => {
        const p = document.createElement('p');
        p.className = 'empty';
        p.textContent = message;
        content.replaceChildren(p);
    };

    const renderDashboard = (data) => {
        const grid = document.createElement('div');
        grid.className = 'metric-grid';

        if (data && typeof data === 'object') {
            for (const [key, value] of Object.entries(data)) {
                const article = document.createElement('article');
                const strong = document.createElement('strong');
                strong.textContent = String(value ?? 0);
                const span = document.createElement('span');
                span.textContent = key;
                article.appendChild(strong);
                article.appendChild(span);
                grid.appendChild(article);
            }
        }
        content.replaceChildren(grid);
    };

    const renderList = (rows) => {
        if (!rows || rows.length === 0) {
            renderEmpty('Nenhum registro encontrado para os filtros selecionados.');
            return;
        }

        const container = document.createElement('div');
        container.className = 'data-list';

        for (const item of rows) {
            const article = document.createElement('article');

            const h3 = document.createElement('h3');
            h3.textContent = item.name || '(Sem nome)';
            article.appendChild(h3);

            const p = document.createElement('p');
            p.textContent = item.status || 'PENDING';
            article.appendChild(p);

            if (item.id) {
                const btn = document.createElement('button');
                btn.type = 'button';
                btn.dataset.critical = item.id;
                btn.textContent = 'Inativar';
                article.appendChild(btn);
            }

            container.appendChild(article);
        }

        content.replaceChildren(container);
    };

    const show = async () => {
        renderEmpty('Carregando dados…');
        if (feedback) feedback.textContent = '';

        try {
            const data = await request(route);
            if (route === 'dashboard') {
                renderDashboard(data);
                return;
            }
            const rows = Array.isArray(data) ? data : [];
            renderList(rows);
        } catch (e) {
            renderEmpty('Não foi possível carregar os dados.');
            if (feedback) feedback.textContent = e.message;
        }
    };

    document.querySelectorAll('[data-tab]').forEach(btn => {
        btn.addEventListener('click', () => {
            const tabName = btn.dataset.tab;
            if (routes[tabName]) {
                route = routes[tabName];
                show();
            }
        });
    });

    const filters = document.querySelector('#hr-filters');
    if (filters) {
        filters.addEventListener('submit', e => {
            e.preventDefault();
            show();
        });
    }

    const newRecordBtn = document.querySelector('#new-hr-record');
    if (newRecordBtn && dialog) {
        newRecordBtn.addEventListener('click', () => dialog.showModal());
    }

    const closeBtn = document.querySelector('[data-close]');
    if (closeBtn && dialog) {
        closeBtn.addEventListener('click', () => dialog.close());
    }

    if (content) {
        content.addEventListener('click', async e => {
            const id = e.target.dataset?.critical;
            if (!id || !confirm('Confirma esta ação crítica?')) return;
            try {
                await request(`${route}/${id}/deactivate`, { method: 'POST' });
                await show();
            } catch (err) {
                if (feedback) feedback.textContent = err.message;
            }
        });
    }

    if (form) {
        form.addEventListener('submit', async e => {
            e.preventDefault();
            if (!form.reportValidity()) return;
            const body = Object.fromEntries(new FormData(form));
            body.amount = Number(body.amount || 0);
            try {
                await request(route, { method: 'POST', body: JSON.stringify(body) });
                if (dialog) dialog.close();
                form.reset();
                await show();
            } catch (err) {
                if (feedback) feedback.textContent = err.message;
            }
        });
    }

    show();
})();
