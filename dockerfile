FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["SRMS.Cloud/SRMS.Cloud.csproj", "SRMS.Cloud/"]
COPY ["SRMS.Core/SRMS.Core.csproj", "SRMS.Core/"]
RUN dotnet restore "SRMS.Cloud/SRMS.Cloud.csproj"
COPY . .
WORKDIR "/src/SRMS.Cloud"
RUN dotnet publish "SRMS.Cloud.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "SRMS.Cloud.dll"]