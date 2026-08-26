# IoT e sensores

Cadastre dispositivo em `/api/iot/devices`; o token é exibido uma vez e persistido como hash. Envie temperatura, umidade, localização, velocidade, tanque, câmara fria, silo, embarcação, veículo ou equipamento em `/api/iot/readings`. Limites são opcionais; violações geram leitura crítica e alerta auditável. Use TLS, rotacione tokens e envie `recordedAt` em UTC.
