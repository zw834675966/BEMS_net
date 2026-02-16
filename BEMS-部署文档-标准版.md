---
document: Deployment Guide
version: "2.0"
status: approved
priority: 3
depends_on: [ADD]
language: zh-CN
deployment_baseline: "纯 Windows 单机（应用 + PostgreSQL17 + TimescaleDB + Mosquitto）"
---

# 建筑能源管理系统 (BEMS) 部署手册

> 文档版本：V2.0  
> 适用架构：纯 Windows 单机部署（应用 + PostgreSQL17 + TimescaleDB + Mosquitto）  
> 部署模式：本机安装，本机服务化运行  
> 部署日期：2026-02-14

---

## 目录

1. [部署前准备](#1-部署前准备)
2. [Windows 本机中间件部署](#2-windows-本机中间件部署)
3. [Windows 应用部署](#3-windows-应用部署)
4. [数据初始化](#4-数据初始化)
5. [服务配置与启动](#5-服务配置与启动)
6. [功能验证](#6-功能验证)
7. [备份与恢复](#7-备份与恢复)
8. [日常运维](#8-日常运维)
9. [常见问题排查](#9-常见问题排查)

---

## 1. 部署前准备

### 1.1 硬件要求

| 角色 | 最低配置 | 推荐配置 |
|------|----------|----------|
| Windows 单机（应用 + 数据库 + MQTT） | 8 核 CPU / 16GB RAM / 200GB SSD | 12 核 CPU / 32GB RAM / 500GB SSD |

### 1.2 软件要求

| 组件 | 版本要求 | 说明 |
|------|----------|------|
| Windows | Windows 11 IoT Enterprise LTSC / Windows Server 2022 | 64 位 |
| PostgreSQL | 17.x | 本机安装 |
| TimescaleDB | 2.19.x（PG17 对应包） | PostgreSQL 时序扩展 |
| Mosquitto | 2.x | MQTT Broker，本机安装 |
| .NET Runtime | 10.0.x | 运行 BEMS 应用 |

### 1.3 网络与端口

| 端口 | 方向 | 用途 |
|------|------|------|
| 5432 | 本机/内网按需开放 | PostgreSQL |
| 1883 | 本机/内网按需开放 | MQTT (非 TLS) |
| 8883 | 本机/内网按需开放 | MQTT (TLS，可选) |
| 443 | 对内开放 | Web API/HMI |
| 502 | 按需开放 | Modbus Slave |

### 1.4 默认账户与密码

> 重要：以下密码仅用于初始部署，生产环境上线后必须修改。

| 账户 | 密码 |
|------|------|
| PostgreSQL `postgres` 管理员 | `EMSzw@18627652962` |
| PostgreSQL `ems` 应用账户 | `EMSzw@18627652962` |
| MQTT `ems` 账户 | `EMSzw@18627652962` |
| MQTT `zw` 账户 | `EMSzw@18627652962` |

---

## 2. Windows 本机中间件部署

### 2.1 安装 PostgreSQL 17

1. 下载并安装 PostgreSQL 17（EnterpriseDB 安装包）。
2. 安装时设置：
   - 超级用户：`postgres`
   - 密码：`EMSzw@18627652962`
   - 端口：`5432`
3. 记录安装目录（示例）：`d:\Program Files\PostgreSQL\17`

验证：

```powershell
Get-Service *postgres*
Test-NetConnection -ComputerName localhost -Port 5432
```

### 2.2 安装 TimescaleDB（本机）

1. 下载并安装与 PostgreSQL 17 对应的 TimescaleDB Windows 安装包。
2. 编辑 `postgresql.conf`（示例路径：`d:\Program Files\PostgreSQL\17\data\postgresql.conf`），确认包含：

```conf
shared_preload_libraries = 'timescaledb'
```

3. 重启 PostgreSQL 服务（服务名以实际为准）：

```powershell
Restart-Service postgresql-x64-17
Get-Service postgresql-x64-17
```

### 2.3 安装 Mosquitto（本机）

1. 下载 Mosquitto 2.x Windows 安装包（64 位）并安装。
2. 安装时勾选 **"Service"** 选项，安装为 Windows 服务。
3. 记录安装目录（示例）：`D:\Program Files\Mosquitto`

> [!IMPORTANT]
> Mosquitto Windows 服务通过 **`MOSQUITTO_DIR`** 系统环境变量定位配置文件 `mosquitto.conf`，而非命令行参数 `-c`。必须正确设置此环境变量。

4. 设置 `MOSQUITTO_DIR` 系统环境变量：

```powershell
[Environment]::SetEnvironmentVariable('MOSQUITTO_DIR', 'D:\Program Files\Mosquitto', 'Machine')
```

5. 将环境变量注入服务注册表（解决服务进程无法读取新设环境变量的问题）：

```powershell
# 以管理员身份运行
Set-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Services\mosquitto' `
  -Name 'Environment' `
  -Value @('MOSQUITTO_DIR=D:\Program Files\Mosquitto') `
  -Type MultiString
```

6. 确认服务 `ImagePath` 正确（仅 `run`，不带 `-c`）：

```powershell
# 查看当前值
(Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\mosquitto').ImagePath
# 期望输出: "D:\Program Files\Mosquitto\mosquitto.exe" run
```

验证：

```powershell
Get-Service mosquitto
Test-NetConnection -ComputerName localhost -Port 1883
```

### 2.4 配置 PostgreSQL 账户与数据库

```powershell
$env:PGPASSWORD = 'EMSzw@18627652962'
& 'D:\Program Files\PostgreSQL\17\bin\psql.exe' -h localhost -U postgres -d postgres -c "DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='ems') THEN CREATE ROLE ems LOGIN PASSWORD 'EMSzw@18627652962'; ELSE ALTER ROLE ems WITH PASSWORD 'EMSzw@18627652962'; END IF; END $$;"
& 'D:\Program Files\PostgreSQL\17\bin\psql.exe' -h localhost -U postgres -d postgres -c "CREATE DATABASE iot_data OWNER ems;" 2>$null
& 'D:\Program Files\PostgreSQL\17\bin\psql.exe' -h localhost -U postgres -d postgres -c "CREATE DATABASE bems OWNER ems;" 2>$null
```

### 2.5 启用 TimescaleDB 扩展

```powershell
$env:PGPASSWORD = 'EMSzw@18627652962'
& 'D:\Program Files\PostgreSQL\17\bin\psql.exe' -h localhost -U postgres -d iot_data -c "CREATE EXTENSION IF NOT EXISTS timescaledb;"
& 'D:\Program Files\PostgreSQL\17\bin\psql.exe' -h localhost -U postgres -d bems -c "CREATE EXTENSION IF NOT EXISTS timescaledb;"
& 'D:\Program Files\PostgreSQL\17\bin\psql.exe' -h localhost -U postgres -d iot_data -c "\dx timescaledb"
```

### 2.6 配置 PostgreSQL 访问策略

编辑 `pg_hba.conf`（示例：`d:\Program Files\PostgreSQL\17\data\pg_hba.conf`），增加：

```conf
host    iot_data   ems    127.0.0.1/32    scram-sha-256
host    bems       ems    127.0.0.1/32    scram-sha-256
```

如果需要内网访问，再按网段增加白名单，不建议直接放开 `0.0.0.0/0`。

重启服务：

```powershell
Restart-Service postgresql-x64-17
```

### 2.7 配置 Mosquitto 账户与服务

#### 2.7.1 创建密码文件

```powershell
# 创建 ems 账户（交互式输入密码）
& 'D:\Program Files\Mosquitto\mosquitto_passwd.exe' -c 'D:\Program Files\Mosquitto\passwd' ems
# 批量添加 zw 账户
& 'D:\Program Files\Mosquitto\mosquitto_passwd.exe' -b 'D:\Program Files\Mosquitto\passwd' zw EMSzw@18627652962
```

#### 2.7.2 编写配置文件

编辑 `D:\Program Files\Mosquitto\mosquitto.conf`：

```conf
listener 1883
allow_anonymous false
password_file D:\Program Files\Mosquitto\passwd
persistence true
persistence_location D:\Program Files\Mosquitto\data\
log_dest file D:\Program Files\Mosquitto\data\mosquitto.log
log_type all
```

#### 2.7.3 设置文件权限

Mosquitto 服务以 **LocalSystem** 身份运行，必须确保 SYSTEM 对相关文件有访问权限：

```powershell
# 以管理员身份运行
New-Item -ItemType Directory -Force -Path 'D:\Program Files\Mosquitto\data'
icacls 'D:\Program Files\Mosquitto\data' /grant 'SYSTEM:(OI)(CI)(F)' /T
icacls 'D:\Program Files\Mosquitto\passwd' /grant 'SYSTEM:(R)'
```

> [!WARNING]
> `log_dest file` 创建的日志文件权限仅限 SYSTEM 用户。如需查看日志，用管理员 PowerShell 读取，或配合 `log_dest stdout` 在前台调试。

#### 2.7.4 启动服务

```powershell
Start-Service mosquitto
Get-Service mosquitto
Test-NetConnection -ComputerName localhost -Port 1883
```

如果服务启动失败，先用前台模式排查：

```powershell
# 前台 verbose 模式（Ctrl+C 退出）
& 'D:\Program Files\Mosquitto\mosquitto.exe' -v -c 'D:\Program Files\Mosquitto\mosquitto.conf'
```

### 2.8 配置 Windows 防火墙

```powershell
New-NetFirewallRule -DisplayName 'BEMS PostgreSQL 5432' -Direction Inbound -Protocol TCP -LocalPort 5432 -Action Allow
New-NetFirewallRule -DisplayName 'BEMS MQTT 1883' -Direction Inbound -Protocol TCP -LocalPort 1883 -Action Allow
New-NetFirewallRule -DisplayName 'BEMS MQTT TLS 8883' -Direction Inbound -Protocol TCP -LocalPort 8883 -Action Allow
New-NetFirewallRule -DisplayName 'BEMS API 443' -Direction Inbound -Protocol TCP -LocalPort 443 -Action Allow
```

### 2.9 中间件部署验证

```powershell
# PostgreSQL
Test-NetConnection -ComputerName localhost -Port 5432

# MQTT
Test-NetConnection -ComputerName localhost -Port 1883

# TimescaleDB
$env:PGPASSWORD = 'EMSzw@18627652962'
& 'C:\Program Files\PostgreSQL\17\bin\psql.exe' -h localhost -U ems -d iot_data -c "SELECT extname, extversion FROM pg_extension WHERE extname='timescaledb';"
```

---

## 3. Windows 应用部署

### 3.1 安装 .NET 10 Runtime

```powershell
winget install Microsoft.DotNet.Runtime.10
dotnet --list-runtimes
```

### 3.2 创建部署目录

```powershell
New-Item -ItemType Directory -Force -Path D:\BEMS
New-Item -ItemType Directory -Force -Path D:\BEMS\Bin
New-Item -ItemType Directory -Force -Path D:\BEMS\config
New-Item -ItemType Directory -Force -Path D:\BEMS\logs
New-Item -ItemType Directory -Force -Path D:\BEMS\certs
```

### 3.3 部署应用文件

将编译产物复制到 `D:\BEMS\Bin\`。

### 3.4 配置 `appsettings.json`

在 `D:\BEMS\config\appsettings.json` 写入：

```json
{
  "ConnectionStrings": {
    "Default": "Host=localhost;Port=5432;Database=iot_data;Username=ems;Password=EMSzw@18627652962;SSL Mode=Prefer"
  },
  "MQTT": {
    "Host": "localhost",
    "Port": 1883,
    "Username": "ems",
    "Password": "EMSzw@18627652962",
    "UseTls": false,
    "ClientId": "BemsGateway"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  }
}
```

### 3.5 配置环境变量

```powershell
[Environment]::SetEnvironmentVariable('ASPNETCORE_ENVIRONMENT', 'Production', 'Machine')
[Environment]::SetEnvironmentVariable('DOTNET_ENVIRONMENT', 'Production', 'Machine')
[Environment]::SetEnvironmentVariable('BEMS_HOME', 'D:\\BEMS', 'Machine')
[Environment]::SetEnvironmentVariable('BEMS_CONFIG_DIR', 'D:\\BEMS\\config', 'Machine')
[Environment]::SetEnvironmentVariable('MOSQUITTO_DIR', 'D:\Program Files\Mosquitto', 'Machine')
```

> [!NOTE]
> `MOSQUITTO_DIR` 在 §2.3 中已设置；此处列出以确保完整性。该变量是 Mosquitto 服务定位配置文件所必需的。

---

## 4. 数据初始化

### 4.1 创建核心表结构

```powershell
$env:PGPASSWORD = 'EMSzw@18627652962'
& 'C:\Program Files\PostgreSQL\17\bin\psql.exe' -h localhost -U ems -d iot_data
```

在 `psql` 中执行：

```sql
CREATE TABLE IF NOT EXISTS tenants (
    tenant_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(100) NOT NULL,
    plan VARCHAR(20) DEFAULT 'Basic',
    status VARCHAR(15) DEFAULT 'Active',
    created_at TIMESTAMPTZ DEFAULT now()
);

CREATE TABLE IF NOT EXISTS devices (
    device_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id UUID NOT NULL REFERENCES tenants(tenant_id),
    name VARCHAR(100) NOT NULL,
    device_type VARCHAR(30) NOT NULL,
    protocol VARCHAR(20) NOT NULL,
    address JSONB NOT NULL,
    register_map JSONB,
    status VARCHAR(15) DEFAULT 'Offline',
    last_seen_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ DEFAULT now(),
    updated_at TIMESTAMPTZ DEFAULT now()
);

CREATE INDEX idx_devices_tenant ON devices(tenant_id);

CREATE TABLE IF NOT EXISTS telemetry (
    tenant_id UUID NOT NULL,
    device_id UUID NOT NULL,
    ts TIMESTAMPTZ NOT NULL,
    key VARCHAR(30) NOT NULL,
    value_number DOUBLE PRECISION,
    value_string VARCHAR(200),
    value_bool BOOLEAN,
    json_value JSONB
);

SELECT create_hypertable('telemetry', 'ts', chunk_time_interval => INTERVAL '7 days', if_not_exists => TRUE);

CREATE INDEX idx_telemetry_device_ts ON telemetry(tenant_id, device_id, ts DESC);
CREATE INDEX idx_telemetry_key ON telemetry(tenant_id, key, ts DESC);

ALTER TABLE telemetry SET (
    timescaledb.compress,
    timescaledb.compress_segmentby = 'tenant_id, device_id, key',
    timescaledb.compress_orderby = 'ts DESC'
);

SELECT add_compression_policy('telemetry', INTERVAL '30 days', if_not_exists => TRUE);
```

### 4.2 创建初始租户

```sql
INSERT INTO tenants (name, plan, status)
VALUES ('默认租户', 'Enterprise', 'Active')
ON CONFLICT DO NOTHING;

SELECT * FROM tenants;
```

---

## 5. 服务配置与启动

### 5.1 启动 Gateway 服务

```powershell
cd D:\BEMS\Bin
.\Gateway.exe
```

生产环境建议注册 Windows 服务：

```powershell
sc create BEMSGateway binPath= "D:\BEMS\Bin\Gateway.exe" start= auto
sc failure BEMSGateway reset= 86400 actions= restart/60000/restart/60000/restart/60000
sc start BEMSGateway
sc query BEMSGateway
```

### 5.2 启动 Web API 服务

```powershell
sc create BEMSApi binPath= "D:\BEMS\Bin\Bems.Api.exe" start= auto
sc start BEMSApi
sc query BEMSApi
```

### 5.3 启动状态检查

```powershell
Get-Service BEMSGateway, BEMSApi, postgresql-x64-17, mosquitto
netstat -ano | Select-String "443|5432|1883|8883|502"
Get-Content D:\BEMS\logs\*.log -Tail 50
```

---

## 6. 功能验证

### 6.1 数据库连接

```powershell
Test-NetConnection -ComputerName localhost -Port 5432
```

### 6.2 MQTT 连接

```powershell
Test-NetConnection -ComputerName localhost -Port 1883
```

### 6.3 API 验证

```powershell
$body = @{ username = 'admin'; password = 'admin123' } | ConvertTo-Json
$token = Invoke-RestMethod -Uri 'https://localhost:443/api/auth/login' -Method Post -Body $body -ContentType 'application/json'
$headers = @{ Authorization = "Bearer $token" }
Invoke-RestMethod -Uri 'https://localhost:443/api/tenants' -Headers $headers
Invoke-RestMethod -Uri 'https://localhost:443/api/devices' -Headers $headers
```

### 6.4 核心指标

| 指标 | 目标值 | 验证方法 |
|------|--------|----------|
| 控制响应时间 | < 2s | 发送控制指令，记录响应时间 |
| 展示链路延迟 | < 500ms | 采集到界面的端到端延迟 |
| 闭环控制成功率 | >= 99% | 统计成功确认次数 |
| 补传成功率 | >= 99.9% | 断网恢复后核对补传完整性 |

---

## 7. 备份与恢复

### 7.1 自动备份脚本（PowerShell）

创建 `D:\BEMS\scripts\bems-backup.ps1`：

```powershell
$backupDir = 'D:\BEMS\backup'
$date = Get-Date -Format 'yyyy-MM-dd'
$dbName = 'iot_data'
$pgDump = 'C:\Program Files\PostgreSQL\17\bin\pg_dump.exe'

New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
$env:PGPASSWORD = 'EMSzw@18627652962'
& $pgDump -h localhost -U ems -d $dbName -F c -f "$backupDir\bems_$date.dump"

Get-ChildItem $backupDir -Filter 'bems_*.dump' |
  Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-7) } |
  Remove-Item -Force
```

创建计划任务（每天 02:00）：

```powershell
schtasks /Create /SC DAILY /ST 02:00 /TN "BEMS-DB-Backup" /TR "powershell -ExecutionPolicy Bypass -File D:\BEMS\scripts\bems-backup.ps1" /F
```

### 7.2 手动备份

```powershell
$env:PGPASSWORD = 'EMSzw@18627652962'
& 'C:\Program Files\PostgreSQL\17\bin\pg_dump.exe' -h localhost -U ems -d iot_data -F c -f "D:\BEMS\backup\bems_manual_$(Get-Date -Format yyyy-MM-dd).dump"
```

### 7.3 数据恢复

```powershell
sc stop BEMSGateway
sc stop BEMSApi

$env:PGPASSWORD = 'EMSzw@18627652962'
& 'C:\Program Files\PostgreSQL\17\bin\pg_restore.exe' -h localhost -U ems -d iot_data -c "D:\BEMS\backup\bems_2026-02-14.dump"

sc start BEMSGateway
sc start BEMSApi
```

---

## 8. 日常运维

### 8.1 日常检查清单

| 检查项 | 频率 | 方法 |
|--------|------|------|
| 服务状态 | 每日 | `Get-Service BEMSGateway,BEMSApi,postgresql-x64-17,mosquitto` |
| 磁盘空间 | 每日 | `Get-Volume` |
| 数据库连通 | 每日 | `Test-NetConnection localhost -Port 5432` |
| 错误日志 | 每日 | `Get-Content D:\BEMS\logs\*.log -Tail 100` |
| 备份文件 | 每周 | `Get-ChildItem D:\BEMS\backup` |

### 8.2 定期维护

| 任务 | 频率 | 说明 |
|------|------|------|
| 日志清理 | 每周 | 清理 30 天前日志 |
| 备份清理 | 每周 | 清理超过保留期的备份 |
| 补丁更新 | 每月 | 更新 Windows 与中间件补丁 |
| 性能评估 | 每季度 | 检查数据库容量与查询性能 |

---

## 9. 常见问题排查

### 9.1 数据库连接失败

```powershell
Get-Service postgresql-x64-17
Test-NetConnection -ComputerName localhost -Port 5432
Get-Content 'C:\Program Files\PostgreSQL\17\data\pg_hba.conf'
```

### 9.2 MQTT 连接失败

**基础检查：**

```powershell
Get-Service mosquitto
Test-NetConnection -ComputerName localhost -Port 1883
& 'D:\Program Files\Mosquitto\mosquitto_pub.exe' -h localhost -p 1883 -u ems -P 'EMSzw@18627652962' -t test -m hello
```

**常见故障与解决方案：**

| 现象 | 原因 | 解决方法 |
|------|------|----------|
| 服务状态 Stopped，事件日志 7009/7000 超时 | `MOSQUITTO_DIR` 环境变量对服务不可见 | 设置服务注册表 `Environment` 值（见 §2.3 步骤 5） |
| 服务启动后立即停止 | `mosquitto.log` 权限冲突（SYSTEM 创建后其他用户无法写入） | 删除旧日志文件并重新启动 |
| 服务启动后立即停止 | `password_file` 路径错误或 SYSTEM 无读取权限 | 检查路径、执行 `icacls passwd /grant 'SYSTEM:(R)'` |
| 端口 1883 被占用 | 有残留的 Mosquitto 前台进程 | `Get-Process mosquitto` 后 `Stop-Process` |

**前台排查：**

```powershell
# 先停止服务，再用前台 verbose 模式诊断
Stop-Service mosquitto -ErrorAction SilentlyContinue
& 'D:\Program Files\Mosquitto\mosquitto.exe' -v -c 'D:\Program Files\Mosquitto\mosquitto.conf'
```

**服务注册表验证：**

```powershell
# 检查 ImagePath 和 Environment
$svcKey = 'HKLM:\SYSTEM\CurrentControlSet\Services\mosquitto'
(Get-ItemProperty $svcKey).ImagePath
(Get-ItemProperty $svcKey).Environment
```

### 9.3 应用服务启动失败

```powershell
Get-EventLog -LogName Application -Newest 100 | Where-Object { $_.Source -match 'BEMS|.NET' }
Get-Content D:\BEMS\config\appsettings.json
Get-Content D:\BEMS\logs\*.log -Tail 200
```

---

## 附录

### 附录 A：快速命令参考（Windows）

```powershell
# 服务管理
sc query BEMSGateway
sc query BEMSApi
Get-Service postgresql-x64-17, mosquitto
Restart-Service postgresql-x64-17
Restart-Service mosquitto

# 网络测试
Test-NetConnection -ComputerName localhost -Port 5432
Test-NetConnection -ComputerName localhost -Port 1883

# PostgreSQL 连接
$env:PGPASSWORD='EMSzw@18627652962'
& 'C:\Program Files\PostgreSQL\17\bin\psql.exe' -h localhost -U ems -d iot_data
```

### 附录 B：默认账户信息

| 账户类型 | 用户名 | 初始密码 |
|----------|--------|----------|
| 数据库应用账户 | ems | EMSzw@18627652962 |
| PostgreSQL 管理员 | postgres | EMSzw@18627652962 |
| MQTT 账户 | ems | EMSzw@18627652962 |
| MQTT 账户 | zw | EMSzw@18627652962 |
| BEMS 管理账户 | admin | admin123 |

> 重要：首次登录后请立即修改默认密码。

---

**文档结束**
