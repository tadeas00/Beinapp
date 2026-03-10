FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["EduApp.Test3.Web/EduApp.Test3.Web.csproj", "EduApp.Test3.Web/"]
COPY ["EduApp.Test3.Shared/EduApp.Test3.Shared.csproj", "EduApp.Test3.Shared/"]

RUN dotnet restore "EduApp.Test3.Web/EduApp.Test3.Web.csproj"

COPY . .
WORKDIR "/src/EduApp.Test3.Web"
RUN dotnet build "EduApp.Test3.Web.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "EduApp.Test3.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "EduApp.Test3.Web.dll"]