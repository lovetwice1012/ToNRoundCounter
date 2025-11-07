# Backend Dockerfile
FROM node:18-alpine

WORKDIR /app

COPY package*.json ./
RUN npm ci --only=production

COPY dist ./dist
COPY src ./src

ENV NODE_ENV=production
EXPOSE 8080

CMD ["npm", "start"]
