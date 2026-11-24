# Build the API, then run it on the ASP.NET Core runtime image as a non-root user.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/ src/
RUN dotnet publish src/TransactionalOutbox.Api/TransactionalOutbox.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./
RUN mkdir -p /app/data && chown "$APP_UID" /app/data
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ConnectionStrings__Subscriptions="Data Source=/app/data/subscriptions.db" \
    ConnectionStrings__Billing="Data Source=/app/data/billing.db"
EXPOSE 8080
ENTRYPOINT ["dotnet", "TransactionalOutbox.Api.dll"]
