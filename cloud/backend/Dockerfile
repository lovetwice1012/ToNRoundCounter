# Backend Dockerfile
FROM node:18-alpine

WORKDIR /app

# Copy shared package first
COPY shared ./shared
WORKDIR /app/shared
RUN npm ci && npm run build

# Setup backend
WORKDIR /app/backend

# Copy backend package files
COPY backend/package*.json ./
COPY backend/tsconfig.json ./

# Install all dependencies (including devDependencies for building)
RUN npm ci

# Copy source code
COPY backend/src ./src

# Build TypeScript
RUN npm run build

# Remove devDependencies
RUN npm prune --production

ENV NODE_ENV=production
EXPOSE 8080

CMD ["npm", "start"]
