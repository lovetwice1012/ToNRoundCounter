# Cloudflare Tunnel deployment

This deployment assumes the public Cloudflare hostname is:

```text
toncloud.sprink.cloud
```

In Cloudflare Zero Trust, create a tunnel and add this public hostname:

```text
Hostname: toncloud.sprink.cloud
Service:  http://frontend:80
```

Copy the generated tunnel token into `cloud/.env`:

```env
CLOUDFLARED_TUNNEL_TOKEN=your-cloudflare-tunnel-token
FRONTEND_URL=https://toncloud.sprink.cloud
```

Then start the stack from the `cloud` directory:

```bash
docker compose up -d --build
```

Only the `cloudflared` container needs to reach the public internet. MariaDB,
backend, and frontend are kept on the Docker network and are not published as
host ports by `docker-compose.yml`.

For a fresh Ubuntu server:

```bash
sudo apt-get update && sudo apt-get install -y ca-certificates curl git gnupg openssl && sudo install -m 0755 -d /etc/apt/keyrings && curl -fsSL https://download.docker.com/linux/ubuntu/gpg | sudo gpg --dearmor -o /etc/apt/keyrings/docker.gpg && sudo chmod a+r /etc/apt/keyrings/docker.gpg && . /etc/os-release && echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/ubuntu $VERSION_CODENAME stable" | sudo tee /etc/apt/sources.list.d/docker.list >/dev/null && sudo apt-get update && sudo apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin && git clone https://github.com/lovetwice1012/ToNRoundCounter.git && cd ToNRoundCounter/cloud && cp .env.example .env && sed -i "s/^DB_ROOT_PASSWORD=.*/DB_ROOT_PASSWORD=$(openssl rand -hex 24)/; s/^DB_PASSWORD=.*/DB_PASSWORD=$(openssl rand -hex 24)/; s/^JWT_SECRET=.*/JWT_SECRET=$(openssl rand -hex 32)/; s/^ACCESS_KEY=.*/ACCESS_KEY=$(openssl rand -hex 16)/" .env && nano .env && sudo docker compose up -d --build
```

Before the final `docker compose up`, paste the tunnel token into `.env`.

If `apt-get update` fails because an unrelated third-party repository is broken
for example `download.konghq.com`, disable that source and rerun the installer:

```bash
sudo mkdir -p /root/disabled-apt-sources && for f in /etc/apt/sources.list.d/*.list; do grep -q 'download.konghq.com' "$f" && sudo mv "$f" "/root/disabled-apt-sources/$(basename "$f").disabled"; done && sudo apt-get update
```
