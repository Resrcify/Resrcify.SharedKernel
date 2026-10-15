# Built from the repository root, as the sample references the SharedKernel projects themselves:
#   docker build -f samples/Resrcify.SharedKernel.WebApiExample/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo
COPY Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/ src/
COPY samples/Resrcify.SharedKernel.WebApiExample/ samples/Resrcify.SharedKernel.WebApiExample/
RUN dotnet publish samples/Resrcify.SharedKernel.WebApiExample/src/Resrcify.SharedKernel.WebApiExample.Web/Resrcify.SharedKernel.WebApiExample.Web.csproj \
    -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 11000
ENV ASPNETCORE_URLS=http://+:11000
COPY --from=build /app/publish .
USER app
ENTRYPOINT ["dotnet", "Resrcify.SharedKernel.WebApiExample.Web.dll"]
