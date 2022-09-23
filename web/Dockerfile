FROM node:alpine as builder

ADD ./package.json /app/package.json
WORKDIR /app/
RUN yarn install
ADD . /app/
RUN yarn build

FROM nginx:alpine
COPY --from=builder /app/build /usr/share/nginx/html