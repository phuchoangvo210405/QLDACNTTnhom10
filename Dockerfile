# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["QLDACNTTnhom10/QLDACNTTnhom10.csproj", "QLDACNTTnhom10/"]
RUN dotnet restore "QLDACNTTnhom10/QLDACNTTnhom10.csproj"

COPY QLDACNTTnhom10/ QLDACNTTnhom10/
WORKDIR "/src/QLDACNTTnhom10"
RUN dotnet publish "QLDACNTTnhom10.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "QLDACNTTnhom10.dll"]
