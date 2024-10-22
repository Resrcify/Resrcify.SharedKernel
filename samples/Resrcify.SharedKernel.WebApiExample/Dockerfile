FROM mcr.microsoft.com/dotnet/sdk:8.0 AS base
WORKDIR /app
EXPOSE 11000

ENV ASPNETCORE_URLS=http://+:11000
ENV ASPNETCORE_ENVIRONMENT ASPNETCORE_ENVIRONMENT

# Creates a non-root user with an explicit UID and adds permission to access the /app folder
# For more info, please refer to https://aka.ms/vscode-docker-dotnet-configure-containers
RUN adduser -u 5678 --disabled-password --gecos "" appuser && chown -R appuser /app
USER appuser

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

# copy all the layers' csproj files into respective folders
COPY ["src/Resrcify.SharedKernel.WebApiExample.Domain/Resrcify.SharedKernel.WebApiExample.Domain.csproj", "Resrcify.SharedKernel.WebApiExample.Domain/"]
COPY ["src/Resrcify.SharedKernel.WebApiExample.Application/Resrcify.SharedKernel.WebApiExample.Application.csproj", "Resrcify.SharedKernel.WebApiExample.Application/"]
COPY ["src/Resrcify.SharedKernel.WebApiExample.Infrastructure/Resrcify.SharedKernel.WebApiExample.Infrastructure.csproj", "Resrcify.SharedKernel.WebApiExample.Infrastructure/"]
COPY ["src/Resrcify.SharedKernel.WebApiExample.Persistence/Resrcify.SharedKernel.WebApiExample.Persistence.csproj", "Resrcify.SharedKernel.WebApiExample.Persistence/"]
COPY ["src/Resrcify.SharedKernel.WebApiExample.Presentation/Resrcify.SharedKernel.WebApiExample.Presentation.csproj", "Resrcify.SharedKernel.WebApiExample.Presentation/"]
COPY ["src/Resrcify.SharedKernel.WebApiExample.Web/Resrcify.SharedKernel.WebApiExample.Web.csproj", "Resrcify.SharedKernel.WebApiExample.Web/"]

RUN dotnet restore "Resrcify.SharedKernel.WebApiExample.Web/Resrcify.SharedKernel.WebApiExample.Web.csproj"

# WORKDIR /app
COPY . .
RUN dotnet build -c Release --property:OutputPath=/app/build

FROM build AS publish
RUN dotnet publish -c Release --property:PublishDir=/app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Resrcify.SharedKernel.WebApiExample.Web.dll"]
