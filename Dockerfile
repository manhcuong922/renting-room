# syntax=docker/dockerfile:1
# Image API. Không chứa bí mật: mọi khóa truyền lúc chạy qua biến môi trường (xem .env.prod.example).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props renting_room.sln ./
COPY src/renting_room.Domain/*.csproj src/renting_room.Domain/
COPY src/renting_room.Application/*.csproj src/renting_room.Application/
COPY src/renting_room.Infrastructure/*.csproj src/renting_room.Infrastructure/
COPY src/renting_room.Api/*.csproj src/renting_room.Api/
RUN dotnet restore src/renting_room.Api/renting_room.Api.csproj
COPY src/ src/
RUN dotnet publish src/renting_room.Api/renting_room.Api.csproj -c Release --no-restore -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 ASPNETCORE_ENVIRONMENT=Production
COPY --from=build /out .
# Thư mục ghi được cho user "app" (volume đặt tên mới tạo sẽ thừa hưởng quyền này): khóa DataProtection và file đính kèm Local (M09).
RUN mkdir -p /keys /data/files && chown app:app /keys /data/files
# Image aspnet có sẵn user không phải root tên "app".
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "renting_room.Api.dll"]
