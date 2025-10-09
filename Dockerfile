# ===== Build Stage =====
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copia apenas os arquivos de projeto para otimizar cache do Docker
COPY *.csproj ./
RUN dotnet restore

# Copia o restante do código
COPY . .

# Publica a aplicação
RUN dotnet publish -c Release -o /app/publish

# ===== Runtime Stage =====
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Copia os arquivos publicados do build stage
COPY --from=build /app/publish .

# Expõe a porta usada pelo Render
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

# Executa o DLL compilado (não o csproj)
ENTRYPOINT ["dotnet", "CvAssistantWeb.dll"]

