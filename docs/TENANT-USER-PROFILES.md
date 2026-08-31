# Usuários, perfis e permissões por tenant

Usuários podem entrar com e-mail, CPF normalizado ou, para o administrador local, documento da organização. Credenciais inválidas devolvem a mesma resposta. Senhas usam PBKDF2 e nunca são persistidas ou logadas em claro. Perfis, vínculos e permissões carregam `tenant_id`; o perfil primário é único por vínculo. Permissão deve proteger menu, ação e endpoint. O cliente nunca cria super administrador nem altera o próprio plano.
