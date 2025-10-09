# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copia apenas o .csproj (ajuste o nome se estiver em uma subpasta)
COPY CvAssistantWeb.csproj ./
RUN dotnet restore

# Copia o restante do código
COPY . .
RUN dotnet publish -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

# Executa o DLL compilado (NÃO o .csproj)
ENTRYPOINT ["dotnet", "CvAssistantWeb.dll"]

