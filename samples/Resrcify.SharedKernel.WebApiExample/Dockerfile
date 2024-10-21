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

# Copy project files
COPY ["samples/Resrcify.SharedKernel.WebApiExample/", "Resrcify.SharedKernel.WebApiExample/"]

# Copy project references
COPY ["src/Resrcify.SharedKernel.ResultFramework/Resrcify.SharedKernel.ResultFramework.csproj", "Resrcify.SharedKernel.ResultFramework/"]
COPY ["src/Resrcify.SharedKernel.DomainDrivenDesign/Resrcify.SharedKernel.DomainDrivenDesign.csproj", "Resrcify.SharedKernel.DomainDrivenDesign/"]
COPY ["src/Resrcify.SharedKernel.Messaging/Resrcify.SharedKernel.Messaging.csproj", "Resrcify.SharedKernel.Messaging/"]
COPY ["src/Resrcify.SharedKernel.Repository/Resrcify.SharedKernel.Repository.csproj", "Resrcify.SharedKernel.Repository/"]
COPY ["src/Resrcify.SharedKernel.Web/Resrcify.SharedKernel.Web.csproj", "Resrcify.SharedKernel.Web/"]

WORKDIR /Resrcify.SharedKernel.WebApiExample

RUN dotnet restore "Resrcify.SharedKernel.WebApiExample.Web/Resrcify.SharedKernel.WebApiExample.Web.csproj"

COPY . .
RUN dotnet build "Resrcify.SharedKernel.WebApiExample.Web/Resrcify.SharedKernel.WebApiExample.Web.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Resrcify.SharedKernel.WebApiExample.Web/Resrcify.SharedKernel.WebApiExample.Web.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Resrcify.SharedKernel.WebApiExample.Web.dll"]
