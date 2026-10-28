# WISE Integrated Middleware

## Document Header

Title: WISE Integrated Middleware

Comment / Function: Middleware software solution for the WISE machine ecosystem.
WISE Machine is developed within the framework of the European Union Horizon Europe research and innovation programme, Grant Agreement No. 101138718.
This work is related to Task 7.5.

Company: MCH-TRONICS sagl

Author: Carlo Benzoni

Target System: WISE production environment

Engineering: C# / .NET

Requirements: See project and deployment requirements

## Change Log

Version: 001.000.000
Date: 2026-05-29
Expert in Charge: Carlo Benzoni
Changes Applied: First released version

`WISE_Integrated_Middleware` is the integration layer of the WISE project.

Its purpose is to act as a bridge between:

- OT/industrial components, such as PLCs and machine controllers
- technical storage, such as HDFS/WebHDFS
- application persistence, such as MongoDB
- external clients, such as HMI or manager applications

In practice, it prevents each application from needing to speak directly with OPC UA, WebHDFS, MongoDB, and Polaris.  
It centralizes integrations, standardizes application contracts, and makes it simpler to evolve the system.

## 1. What It Solves

The starting problem is fragmentation:

- the PLC exposes data via OPC UA
- documents must be stored in HDFS
- metadata must be searchable in MongoDB
- Polaris must be commanded with a dedicated library
- the HMI needs simple and stable APIs

Without middleware, each client would need to integrate all these protocols and systems directly.  
With this middleware, clients speak to a single application layer that handles:

- acquiring PLC telemetry
- persisting data and metadata
- managing document upload/download
- sending commands to Polaris
- exposing diagnostics of component status

## 2. Solution in One Sentence

The solution is a modular .NET middleware, composed of an API host and a Worker host, that uses:

- `OPC UA` to read the PLC
- `MongoDB` to save telemetry and metadata
- `WebHDFS` to save files
- `PolarisCommunication` to command the Polaris controller

## 3. Main Components

The main components of the system are:

### PLC / OPC UA Server

It is the source of machine data.  
The middleware connects to the OPC UA server, explores configured nodes, and reads exposed variables.

### MongoDB

It is the application database.

It is used to save:

- PLC telemetry in the `integration_plc_telemetry` collection
- document metadata in the `integration_file_artifacts` collection

MongoDB does not contain the actual files, but searchable descriptive data.

It is also used to save:

- persisted production designs in the `production_designs` collection
- persisted production jobs in the `production_jobs` collection

### HDFS / WebHDFS

It is the file storage system.

The middleware uploads documents here and uses MongoDB to save their metadata.  
This way:

- HDFS preserves content
- MongoDB makes documents searchable and manageable

### Polaris Controller

It is the machine controller for G-code and axis commands.

The middleware currently supports:

- reading basic controller info
- uploading G-code files
- remote G-code execution
- enabling an axis
- reading G-code motor status

### HMI / Manager App / HTTP Client

They are consumers of the middleware.

They can:

- query REST APIs
- upload documents
- read telemetry
- launch Polaris commands
- read component status

### Middleware.Worker

It is the background process.

It handles:

- periodic polling of PLC telemetry
- console commands for testing and manual operations

### Middleware.Api

It is the HTTP process.

It handles:

- exposing REST endpoints to HMI and external clients
- managing document upload/download
- exposing telemetry and diagnostics
- serving as entry point for Polaris commands

## 4. Repository Architecture

The solution contains these projects:

- `src/Middleware.Domain`
- `src/Middleware.Application`
- `src/Middleware.Infrastructure`
- `src/Middleware.Api`
- `src/Middleware.Worker`
- `src/ProcessCoordinator`

### Middleware.Domain

Contains domain models and shared records:

- PLC sample
- telemetry payload
- document metadata
- file content
- Polaris command results

There is no integration logic here, just data structure.

### Middleware.Application

Contains:

- abstract contracts of integrations
- application services that orchestrate use cases

Examples:

- `IOpcUaAdapter`
- `IFileStorageAdapter`
- `IPolarisGCodeAdapter`
- `IPlcTelemetrySampleRepository`
- `IFileArtifactMetadataRepository`

and the services:

- `MiddlewareIngestionService`
- `FileUploadService`
- `PolarisGCodeService`
- `PolarisInfoService`

### Middleware.Infrastructure

Contains concrete implementations:

- [`OpcUaAdapter`](c:\Users\carlo\source\repos\WISE_Integrated_Middleware\src\Middleware.Infrastructure\Adapters\OpcUaAdapter.cs)
- `WebHdfsFileStorageAdapter`
- `PolarisGCodeAdapter`
- `MongoPlcTelemetrySampleRepository`
- `MongoFileArtifactMetadataRepository`
- diagnostic services

This is the layer where technical details live.

### Middleware.Api

Exposes REST controllers.

Main controllers:

- `DocumentsController`
- `TelemetryController`
- `PolarisController`
- `SystemStatusController`

### Middleware.Worker

Executes background jobs.

Main hosted services:

- `OpcUaIngestionWorker`
- `ConsoleCommandWorker`

### ProcessCoordinator

It is the process that orchestrates production execution.

It handles:

- reading production jobs persisted in MongoDB
- retrieving the selected design and related files
- verifying minimum Polaris readiness
- executing G-codes in the sequence defined by the design
- updating job status step by step

This way the system clearly distinguishes:

- atomic APIs, used by HMI and clients
- workflow orchestration, executed by the coordinator

## 5. How the Solution Works

### 5.1 PLC → MongoDB Flow

The Worker starts a periodic polling loop.

Steps:

1. `OpcUaIngestionWorker` wakes up the loop every `PollingIntervalMs`
2. calls `MiddlewareIngestionService`
3. `MiddlewareIngestionService` uses `OpcUaAdapter`
4. `OpcUaAdapter`:
   - connects to the OPC UA server
   - explores the configured root node
   - resolves available variables
   - reads current values
5. the sample is converted to `PlcTelemetrySample`
6. `MongoPlcTelemetrySampleRepository` saves it to MongoDB

The middleware supports dynamic discovery of OPC UA variables under the configured node.  
This reduces dependency on hardcoded variable mapping.

### 5.1.1 TwinCAT ADS → MongoDB Flow

TwinCAT collection is implemented as a separate Worker module and uses its own configuration file, `twincat-telemetry.json`.

The file defines the remote AMS Net ID, PLC runtime ADS port, source ID, enable mode, and polling groups. Each group owns its variable list and polling interval: process signals are polled every second, temperatures every 20 seconds, and power measurements every 5 seconds. Plane and axis values are excluded. In `MiddlewareCommand` mode, `Enabled` sets the startup state and the Worker console can start or stop collection while it is running. In `PlcSymbol` mode, the Worker follows a configured PLC Boolean and fails closed if that signal cannot be read. ADS access to the enable symbol is read-only; it does not write to the PLC.

The Worker stores these records in MongoDB collection `integration_twincat_telemetry`, separate from the existing OPC UA collection. Each document includes the source ID, group ID, observed and ingested timestamps, values, and any per-variable read errors. This first version does not add TwinCAT records to the existing `/api/telemetry` endpoints.

When the Worker is running interactively with `EnableMode` set to `MiddlewareCommand`, enter `twincat start`, `twincat stop`, or `twincat status` in its console. `Enabled` in `twincat-telemetry.json` controls the startup state; console changes apply only until the Worker restarts. To control collection from the TwinCAT HMI, set `EnableMode` to `PlcSymbol` and configure `EnableSymbol` with a PLC `BOOL` that the HMI/operator can change. The worker reads that symbol at `EnablePollingIntervalMs` and starts or stops saving accordingly. The BOOL must be added and deployed in the PLC project before enabling this mode. If the symbol is unreadable or the ADS connection is lost, automatic collection stops.

### 5.2 Documents → HDFS + MongoDB Flow

Steps:

1. a client calls `POST /api/documents/upload`
2. `DocumentsController` saves files to a temporary directory
3. builds a list of `FileArtifactUploadRequest`
4. calls `FileUploadService`
5. `FileUploadService` uses:
   - `WebHdfsFileStorageAdapter` to upload the file to HDFS
   - `MongoFileArtifactMetadataRepository` to save metadata
6. files are organized in HDFS by `use case` and `category`
7. metadata becomes queryable via API

This separation is intentional:

- HDFS stores content
- MongoDB stores the catalog

### 5.3 Documents → Search / Download / Delete Flow

Documents are not searched directly on HDFS.  
Search happens on metadata in MongoDB.

This makes simple and fast:

- filter by name
- filter by use case
- filter by category
- filter by user
- single download
- multiple download as zip
- delete metadata + HDFS file

### 5.4 Polaris Commands Flow

Typical steps:

1. the client calls an endpoint `/api/polaris/*`
2. `PolarisController` forwards to `PolarisGCodeService` or `PolarisInfoService`
3. the service uses `PolarisGCodeAdapter`
4. the adapter communicates with Polaris controller via `PolarisCommunication` library

Currently supported commands:

- read controller info
- read G-code status
- run a remote file
- upload-and-run
- enable an axis

### 5.5 Design → Job → ProcessCoordinator Flow

This is the new flow that connects HMI, storage, and machine.

Steps:

1. the user configures a use case from the HMI
2. the HMI saves a `ProductionDesign` via `POST /api/production/designs`
3. the design contains:
   - unique identifier
   - use case
   - selected product/individual
   - parameter snapshot for each step
   - selected recipe for each step
   - link to part program documents on HDFS/MongoDB
4. when the user decides to produce, the HMI creates a `ProductionJob`
5. `ProcessCoordinator` picks the first `Queued` job
6. retrieves the design from MongoDB
7. downloads related part programs from the document repository
8. loads them on Polaris and executes them in sequence
9. updates the job in MongoDB as:
   - `Validating`
   - `Running`
   - `Completed`
   - `Failed`
10. if an error occurs, the job goes to `Failed` and the flow stops

This strategy is intentionally simple:

- no automatic retry
- no automatic recovery
- restart/manual intervention required after a fail

The advantage is that all context remains persisted and recoverable:

- design
- related files
- job
- execution events

### 5.6 Diagnostics Flow

`MiddlewareDiagnosticsService` verifies reachability of:

- MongoDB
- WebHDFS NameNode
- WebHDFS DataNode
- OPC UA
- Polaris

The result is exposed via:

- `GET /api/system/status`

Additionally, the same checks are reused in startup logs.

## 6. Main APIs

### Documents

- `GET /api/documents`
- `GET /api/documents/{documentId}`
- `GET /api/documents/{documentId}/download`
- `DELETE /api/documents/{documentId}`
- `POST /api/documents/upload`
- `POST /api/documents/download-zip`

### Telemetry

- `GET /api/telemetry/variables`
- `GET /api/telemetry/series`
- `GET /api/telemetry/series/csv`

### Polaris

- `GET /api/polaris/config`
- `GET /api/polaris/info`
- `GET /api/polaris/gcode/status`
- `POST /api/polaris/run-remote`
- `POST /api/polaris/upload-and-run`
- `POST /api/polaris/enable-axis`

### Production

- `GET /api/production/designs`
- `GET /api/production/designs/{designId}`
- `POST /api/production/designs`
- `GET /api/production/jobs`
- `GET /api/production/jobs/{jobId}`
- `POST /api/production/jobs`

### Diagnostics

- `GET /api/system/status`

## 7. Worker Console Commands

When `Middleware.Worker` is running, it exposes useful terminal commands for testing and manual operations.

Current commands:

- `upload-files <username>`
- `polaris-run <localFileNameOrPath> [remoteFileName]`
- `polaris-run-remote <remoteFileName>`
- `polaris-enable-axis <axisName>`
- `polaris-gcode-status`
- `polaris-info`

For a one-shot, read-only TwinCAT ADS check, run the Worker with the `--twincat-read` command-line mode. It reads the selected configured group and prints values to the terminal without starting background workers or saving telemetry to MongoDB:

```powershell
dotnet run --project .\src\Middleware.Worker -- --twincat-read process process.pressure.a_dst,process.pressure.b_dst
```

The optional third argument selects comma-separated variable keys from that group:

```powershell
dotnet run --project .\src\Middleware.Worker -- --twincat-read temperature temperature.zone01.actual,temperature.zone01.setpoint
```

This mode is designed primarily for:

- quick testing
- local debugging
- integration validation without HMI

## 8. Configuration

Shared configuration is found in the file:

- [`appsettings.json`](c:\Users\carlo\source\repos\WISE_Integrated_Middleware\appsettings.json)

Both `Middleware.Api` and `Middleware.Worker` read the same file.

The TwinCAT ADS module has a separate `twincat-telemetry.json`, loaded only by `Middleware.Worker`.

Main sections:

- `Middleware.Mongo`
- `Middleware.OpcUa`
- `Middleware.Storage`
- `Middleware.Polaris`
- `Middleware.ProcessCoordinator`
- `Middleware.Worker`

### MongoDB Configuration

- `ConnectionString`
- `Database`
- `PlcTelemetryCollection`
- `TwinCatTelemetryCollection`
- `FileMetadataCollection`
- `ProductionDesignCollection`
- `ProductionJobCollection`

### OPC UA Configuration

- `PlcId`
- `EndpointUrl`
- `UseSecurity`
- `SecurityMode`
- `SecurityPolicyUri`
- `AutoAcceptUntrustedCertificates`
- `SessionName`
- `SessionTimeoutMs`
- `ReconnectPeriodMs`
- `DiscoveryRootNodeId`
- `RecursiveBrowse`
- `User`

### Storage Configuration

- `UploadFolderPath`
- `DownloadFolderPath`
- `WebHdfsBaseUrl`
- `DataNodeWebHdfsBaseUrl`
- `NameNodeRpcAddress`
- `HdfsFolderPath`
- `HdfsUser`

### Polaris Configuration

- `Enabled`
- `IpAddress`
- `GCodeDirectory`
- `LocalGCodeFolderPath`

### Worker Configuration

- `PollingIntervalMs`

### ProcessCoordinator Configuration

- `IdleDelayMs`
- `GCodeStatusPollingIntervalMs`
- `StepCompletionTimeoutMs`

## 9. Data Persistence

### PLC Telemetry

Collection:

- `integration_plc_telemetry`

Envelope schema:

- `record_type`
- `source_type`
- `source_id`
- `timestamp_utc`
- `ingested_at_utc`
- `payload`

The `payload` currently contains a dictionary of variables read via OPC UA.

### Document Metadata

Collection:

- `integration_file_artifacts`

Contains:

- `document_id`
- file name
- original name
- HDFS path
- size
- user
- mime type
- file type
- use case
- category
- source system

### Document Content

Binary content is saved in HDFS, not in MongoDB.

HDFS structure is of the type:

- `/WISE/{UseCase}/{Category}/{DocumentId}_{OriginalFileName}`

### Production Designs

Collection:

- `production_designs`

Contains:

- `design_id`
- `design_name`
- `use_case`
- `product_identifier`
- `designed_by`
- `designed_at_utc`
- `notes`
- `related_documents`
- `process_steps`

Each `process_step` persists:

- sequence
- process name
- label
- flag `requires_machine_execution`
- selected recipe
- parameter snapshot
- `part_program_document_id`
- part program file name

### Production Jobs

Collection:

- `production_jobs`

Contains:

- `job_id`
- `design_id`
- `design_name`
- `use_case`
- `requested_by`
- `requested_at_utc`
- `status`
- `current_step_sequence`
- `current_step_name`
- `started_at_utc`
- `completed_at_utc`
- `failure_reason`
- `events`

## 10. Current Robustness Status

The middleware is already useful and functional, but should be considered as a solid laboratory/advanced integration base, not yet as a definitive industrial platform.

Strengths:

- clear layering
- unified configuration
- good separation between metadata and file content
- Polaris integration already usable
- system diagnostics present
- persisted production orchestration with separate designs and jobs

Current limitations:

- minimal API security
- automated tests still limited
- some OPC UA APIs use deprecated library methods
- logging improvable with correlation IDs and metrics
- no explicit retention policy for telemetry

## 11. Getting Started if You're New to the Project

If a new person takes over the project, the correct order to understand and use it is this.

### Step 1: Understand the Two Hosts

There are two important executables:

- `Middleware.Api`
- `Middleware.Worker`

The API serves HTTP clients.  
The Worker handles polling and console commands.

### Step 2: Read the Configuration

Open:

- [`appsettings.json`](c:\Users\carlo\source\repos\WISE_Integrated_Middleware\appsettings.json)

and verify:

- OPC UA endpoint
- MongoDB config
- HDFS config
- Polaris config
- worker polling

### Step 3: Understand the Layers

To orient yourself:

- `Domain`: what the data is
- `Application`: what use cases exist
- `Infrastructure`: how to talk to external systems
- `Api`: how clients use the middleware
- `Worker`: what happens in the background

### Step 4: Start the Main Components

To use the middleware locally, you need at least:

1. MongoDB running
2. HDFS/WebHDFS running if you want to use documents
3. OPC UA server running if you want telemetry
4. Polaris running if you want machine commands
5. `ProcessCoordinator` running if you want to execute persisted production jobs

Then:

```powershell
dotnet run --project .\src\Middleware.Api
```

and/or

```powershell
dotnet run --project .\src\Middleware.Worker
```

and, for production orchestration:

```powershell
dotnet run --project .\src\ProcessCoordinator
```

### Step 5: Understand the Flows Before Modifying Code

Before changing anything:

- for PLC, look at `MiddlewareIngestionService` + `OpcUaAdapter`
- for documents, look at `FileUploadService` + `WebHdfsFileStorageAdapter` + MongoDB repository
- for Polaris, look at `PolarisController` + `PolarisGCodeService` + `PolarisGCodeAdapter`
- for production, look at `ProductionController` + `production_*` repository + `ProcessCoordinator`

This avoids touching the wrong layer.

## 12. Key Files to Know

If you need to become independent, these are the files to read first:

- [`appsettings.json`](c:\Users\carlo\source\repos\WISE_Integrated_Middleware\appsettings.json)
- [`src/Middleware.Api/Program.cs`](c:\Users\carlo\source\repos\WISE_Integrated_Middleware\src\Middleware.Api\Program.cs)
- [`src/Middleware.Worker/Program.cs`](c:\Users\carlo\source\repos\WISE_Integrated_Middleware\src\Middleware.Worker\Program.cs)
- [`src/Middleware.Application/Services/MiddlewareIngestionService.cs`](c:\Users\carlo\source\repos\WISE_Integrated_Middleware\src\Middleware.Application\Services\MiddlewareIngestionService.cs)
- [`src/Middleware.Application/Services/FileUploadService.cs`](c:\Users\carlo\source\repos\WISE_Integrated_Middleware\src\Middleware.Application\Services\FileUploadService.cs)
- [`src/Middleware.Application/Services/PolarisGCodeService.cs`](c:\Users\carlo\source\repos\WISE_Integrated_Middleware\src\Middleware.Application\Services\PolarisGCodeService.cs)
- [`src/Middleware.Application/Services/ProductionDesignService.cs`](c:\Users\carlo\source\repos\WISE_Integrated_Middleware\src\Middleware.Application\Services\ProductionDesignService.cs)
- [`src/Middleware.Application/Services/ProductionJobService.cs`](c:\Users\carlo\source\repos\WISE_Integrated_Middleware\src\Middleware.Application\Services\ProductionJobService.cs)
- [`src/Middleware.Infrastructure/Adapters/OpcUaAdapter.cs`](c:\Users\carlo\source\repos\WISE_Integrated_Middleware\src\Middleware.Infrastructure\Adapters\OpcUaAdapter.cs)
- [`src/Middleware.Infrastructure/Adapters/WebHdfsFileStorageAdapter.cs`](c:\Users\carlo\source\repos\WISE_Integrated_Middleware\src\Middleware.Infrastructure\Adapters\WebHdfsFileStorageAdapter.cs)
- [`src/Middleware.Infrastructure/Adapters/PolarisGCodeAdapter.cs`](c:\Users\carlo\source\repos\WISE_Integrated_Middleware\src\Middleware.Infrastructure\Adapters\PolarisGCodeAdapter.cs)
- [`src/Middleware.Api/Controllers/DocumentsController.cs`](c:\Users\carlo\source\repos\WISE_Integrated_Middleware\src\Middleware.Api\Controllers\DocumentsController.cs)
- [`src/Middleware.Api/Controllers/TelemetryController.cs`](c:\Users\carlo\source\repos\WISE_Integrated_Middleware\src\Middleware.Api\Controllers\TelemetryController.cs)
- [`src/Middleware.Api/Controllers/PolarisController.cs`](c:\Users\carlo\source\repos\WISE_Integrated_Middleware\src\Middleware.Api\Controllers\PolarisController.cs)
- [`src/Middleware.Api/Controllers/ProductionController.cs`](c:\Users\carlo\source\repos\WISE_Integrated_Middleware\src\Middleware.Api\Controllers\ProductionController.cs)
- [`src/Middleware.Infrastructure/Services/MiddlewareDiagnosticsService.cs`](c:\Users\carlo\source\repos\WISE_Integrated_Middleware\src\Middleware.Infrastructure\Services\MiddlewareDiagnosticsService.cs)
- [`src/ProcessCoordinator/Services/ProductionWorkflowCoordinator.cs`](c:\Users\carlo\source\repos\WISE_Integrated_Middleware\src\ProcessCoordinator\Services\ProductionWorkflowCoordinator.cs)

## 13. In Summary

If you had to explain the project to a new person in a few lines:

- the `Worker` collects data from the PLC and offers manual commands
- the `Api` exposes REST services for clients and HMI
- the `ProcessCoordinator` executes persisted production workflows
- `Infrastructure` integrates OPC UA, MongoDB, HDFS, and Polaris
- `Application` orchestrates the use cases
- `Domain` defines the shared data

Il middleware e' quindi il punto unico di integrazione tra mondo macchina, storage e applicazioni client.
