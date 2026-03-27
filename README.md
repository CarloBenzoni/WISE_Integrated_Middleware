# WISE Integrated Middleware

Primi casi concreti implementati:

- `PLC -> OPC UA -> Middleware -> MongoDB`
- `Prompt -> cartella locale -> HDFS/WebHDFS + metadata -> MongoDB`
- `Prompt/API -> server Polaris -> upload G-code + run`
- `Prompt/API -> server Polaris -> lettura info standard controller`

## Focus attuale

Il middleware resta intenzionalmente semplice:

- legge tre nodi OPC UA dal PLC:
  - `AxisPosition`
  - `MachineStatus`
  - `AlarmCode`
- costruisce un sample applicativo minimale
- salva il sample in MongoDB
- se riceve il comando `upload-files <username>` da prompt:
  - legge tutti i file dalla cartella configurata
  - li carica su HDFS via WebHDFS
  - salva i metadata file in MongoDB
- se riceve il comando `polaris-run <localFileNameOrPath> [remoteFileName]`:
  - risolve il file locale da path assoluto o dalla cartella G-code configurata
  - lo carica nella directory remota G-code del server Polaris
  - invia il comando di run del G-code
- se riceve il comando `polaris-run-remote <remoteFileName>`:
  - esegue un file G-code gia' presente lato server Polaris
- se riceve il comando `polaris-info`:
  - legge un set minimo di informazioni standard dal controllore Polaris
  - `LinuxKernelVersion`
  - `PCIeDriverVersion`
  - `PolarisCurrentStatus`
  - `PolarisStartupStatus`

Il `ProcessCoordinator` resta nello solution ma in questa fase non partecipa al flusso.

## Progetti rilevanti

- `src/Middleware.Domain`: definisce `PlcTelemetrySample`
- `src/Middleware.Application`: definisce i contratti degli adapter e i servizi applicativi per ingestione, file upload e comando Polaris
- `src/Middleware.Infrastructure`: contiene il client OPC UA reale, i repository MongoDB, WebHDFS e l'adapter Polaris
- `src/Middleware.Worker`: esegue il polling periodico PLC e accetta i comandi prompt per HDFS e Polaris
- `src/Middleware.Api`: espone health/config minima e gli endpoint `api/polaris/*`

## Configurazione

La configurazione si trova negli `appsettings.json` di API e Worker, sotto `Middleware`:

- `Mongo.ConnectionString`
- `Mongo.Database`
- `Mongo.PlcTelemetryCollection`
- `Mongo.FileMetadataCollection`
- `OpcUa.EndpointUrl`
- `OpcUa.DiscoveryRootNodeId`
- `OpcUa.RecursiveBrowse`
- `OpcUa.Nodes.AxisPositionNodeId`
- `OpcUa.Nodes.MachineStatusNodeId`
- `OpcUa.Nodes.AlarmCodeNodeId`
- `Storage.UploadFolderPath`
- `Storage.WebHdfsBaseUrl`
- `Storage.HdfsFolderPath`
- `Storage.HdfsUser`
- `Polaris.Enabled`
- `Polaris.IpAddress`
- `Polaris.GCodeDirectory`
- `Polaris.LocalGCodeFolderPath`
- `Worker.PollingIntervalMs`

## Test locale atteso

Scenario target:

1. MongoDB attivo sul PC.
2. PLC o server OPC UA raggiungibile dal PC.
3. `Middleware.Worker` in esecuzione.
4. I sample letti da OPC UA vengono inseriti nella collection Mongo `integration_plc_telemetry`.
5. Se dal prompt del worker lanci `upload-files <username>`, i file della cartella configurata vengono caricati in HDFS e i metadata finiscono nella collection `integration_file_artifacts`.
6. Se dal prompt del worker lanci `polaris-run example.gc`, il middleware carica `example.gc` nel server Polaris e avvia il run.
7. In alternativa l'API espone:
   - `POST /api/polaris/upload-and-run`
   - `POST /api/polaris/run-remote`
   - `GET /api/polaris/info`

Configurazione di default allineata ai riferimenti WISE già presenti:

- MongoDB: `mongodb://localhost:27017`
- Database MongoDB: `WISE`
- Collection MongoDB telemetria PLC: `integration_plc_telemetry`
- Collection MongoDB metadata file: `integration_file_artifacts`
- PlcId: `S7-1200-Station_1`
- Polaris server IP di default: `192.168.99.57`
- Polaris G-code directory di default: `/home/polaris/PolarisServer/usr/gcode`

## Schema di persistenza

Tutti i documenti salvati in MongoDB usano lo stesso envelope di base:

- `record_type`
- `source_type`
- `source_id`
- `timestamp_utc`
- `ingested_at_utc`
- `payload`

Esempi:

- `integration_plc_telemetry`
  - `record_type = plc_telemetry_sample`
  - `payload.axis_position`
  - `payload.machine_status`
  - `payload.alarm_code`

- `integration_file_artifacts`
  - `record_type = file_artifact_metadata`
  - `payload.file_name`
  - `payload.local_path`
  - `payload.storage_type`
  - `payload.storage_path`
  - `payload.size_bytes`
  - `payload.uploaded_by`
  - `payload.file_type`
