FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/OmniCommerce.Api/OmniCommerce.Api.csproj src/OmniCommerce.Api/
RUN dotnet restore src/OmniCommerce.Api/OmniCommerce.Api.csproj
COPY src/OmniCommerce.Api/ src/OmniCommerce.Api/
RUN dotnet publish src/OmniCommerce.Api/OmniCommerce.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "OmniCommerce.Api.dll"]
