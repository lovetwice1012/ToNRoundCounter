# Frontend Dockerfile
FROM node:18-alpine AS builder

WORKDIR /app

# Copy shared package
COPY shared ./shared

# Build shared package
WORKDIR /app/shared
RUN npm ci && npm run build

# Setup frontend
WORKDIR /app
COPY frontend ./frontend

# Install frontend dependencies (shared is already built in /app/shared)
WORKDIR /app/frontend
RUN npm ci

# Build frontend
RUN npm run build

FROM nginx:alpine

COPY --from=builder /app/frontend/dist /usr/share/nginx/html
COPY frontend/nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80

CMD ["nginx", "-g", "daemon off;"]
