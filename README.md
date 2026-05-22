# WISE Integrated Middleware

`WISE_Integrated_Middleware` e' il livello di integrazione del progetto WISE.

Il suo compito e' fare da ponte tra:

- componenti OT/industriali, come PLC e controller macchina
- storage tecnico, come HDFS/WebHDFS
- persistenza applicativa, come MongoDB
- client esterni, come HMI o applicazioni manager

In pratica evita che ogni applicazione debba parlare direttamente con OPC UA, WebHDFS, MongoDB e Polaris.  
Centralizza le integrazioni, uniforma i contratti applicativi e rende piu' semplice evolvere il sistema.

## 1. Cosa risolve

Il problema di partenza e' la frammentazione:

- il PLC espone dati via OPC UA
- i documenti devono finire su HDFS
- i metadata devono essere cercabili in MongoDB
- Polaris va comandato con una libreria dedicata
- l'HMI ha bisogno di API semplici e stabili

Senza middleware, ogni client dovrebbe integrare direttamente tutti questi protocolli e sistemi.  
Con questo middleware, i client parlano a un solo strato applicativo, che si occupa di:

- acquisire telemetria PLC
- persistire dati e metadata
- gestire upload/download documenti
- inviare comandi a Polaris
- esporre diagnostica dello stato dei componenti

## 2. Soluzione in una frase

La soluzione e' un middleware .NET modulare, composto da un host API e un host Worker, che usa:

- `OPC UA` per leggere il PLC
- `MongoDB` per salvare telemetria e metadata
- `WebHDFS` per salvare i file
- `PolarisCommunication` per comandare il controller Polaris

## 3. Player in gioco

I player principali del sistema sono questi.

### PLC / OPC UA Server

E' la sorgente dei dati macchina.  
Il middleware si connette al server OPC UA, esplora i nodi configurati e legge le variabili esposte.

### MongoDB

E' il database applicativo.

Viene usato per salvare:

- telemetria PLC nella collection `integration_plc_telemetry`
- metadata dei documenti nella collection `integration_file_artifacts`

Mongo non contiene i file veri e propri, ma i dati descrittivi e ricercabili.

Inoltre viene usato anche per salvare:

- design di produzione persistiti nella collection `production_designs`
- job di produzione persistiti nella collection `production_jobs`

### HDFS / WebHDFS

E' lo storage dei file.

Il middleware carica qui i documenti e poi usa Mongo per salvarne i metadata.  
In questo modo:

- HDFS conserva i contenuti
- Mongo rende i documenti cercabili e gestibili

### Polaris Controller

E' il controller macchina per G-code e comandi asse.

Il middleware oggi supporta:

- lettura info base del controller
- upload file G-code
- esecuzione G-code remoto
- enable di un asse
- lettura stato del motore G-code

### HMI / Manager App / Client HTTP

Sono i consumatori del middleware.

Possono:

- interrogare le API REST
- caricare documenti
- leggere telemetria
- lanciare comandi Polaris
- leggere lo stato dei componenti

### Middleware.Worker

E' il processo background.

Si occupa di:

- polling periodico della telemetria PLC
- comandi console per test e operazioni manuali

### Middleware.Api

E' il processo HTTP.

Si occupa di:

- esporre endpoint REST verso HMI e client esterni
- gestire upload/download documenti
- esporre telemetria e diagnostica
- fare da entrypoint per i comandi Polaris

## 4. Architettura del repository

La solution contiene questi progetti:

- `src/Middleware.Domain`
- `src/Middleware.Application`
- `src/Middleware.Infrastructure`
- `src/Middleware.Api`
- `src/Middleware.Worker`
- `src/ProcessCoordinator`

### Middleware.Domain

Contiene i modelli di dominio e i record condivisi:

- sample PLC
- payload telemetria
- metadata documento
- contenuto file
- risultati dei comandi Polaris

Qui non c'e' logica di integrazione, solo struttura dei dati.

### Middleware.Application

Contiene:

- i contratti astratti delle integrazioni
- i servizi applicativi che orchestrano i casi d'uso

Esempi:

- `IOpcUaAdapter`
- `IFileStorageAdapter`
- `IPolarisGCodeAdapter`
- `IPlcTelemetrySampleRepository`
- `IFileArtifactMetadataRepository`

e i servizi:

- `MiddlewareIngestionService`
- `FileUploadService`
- `PolarisGCodeService`
- `PolarisInfoService`

### Middleware.Infrastructure

Contiene le implementazioni concrete:

- [`OpcUaAdapter`](c:\Users\carlo\source\repos\WISE_Integrated_Middleware\src\Middleware.Infrastructure\Adapters\OpcUaAdapter.cs)
- `WebHdfsFileStorageAdapter`
- `PolarisGCodeAdapter`
- `MongoPlcTelemetrySampleRepository`
- `MongoFileArtifactMetadataRepository`
- servizi diagnostici

Questo e' il layer dove vivono i dettagli tecnologici.

### Middleware.Api

Espone i controller REST.

Controller principali:

- `DocumentsController`
- `TelemetryController`
- `PolarisController`
- `SystemStatusController`

### Middleware.Worker

Esegue i job in background.

Hosted service principali:

- `OpcUaIngestionWorker`
- `ConsoleCommandWorker`

### ProcessCoordinator

E' il processo che orchestra l'esecuzione produttiva.

Si occupa di:

- leggere i job di produzione persistiti in MongoDB
- recuperare il design selezionato e i file collegati
- verificare la readiness minima di Polaris
- eseguire i G-code nella sequenza definita dal design
- aggiornare lo stato del job passo per passo

In questo modo il sistema distingue chiaramente:

- API atomiche, usate da HMI e client
- orchestrazione di workflow, eseguita dal coordinator

## 5. Come funziona la soluzione

### 5.1 Flusso PLC -> MongoDB

Il Worker avvia un ciclo di polling periodico.

Passi:

1. `OpcUaIngestionWorker` sveglia il ciclo ogni `PollingIntervalMs`
2. chiama `MiddlewareIngestionService`
3. `MiddlewareIngestionService` usa `OpcUaAdapter`
4. `OpcUaAdapter`:
   - si connette al server OPC UA
   - esplora il nodo root configurato
   - risolve le variabili disponibili
   - legge i valori correnti
5. il sample viene convertito in `PlcTelemetrySample`
6. `MongoPlcTelemetrySampleRepository` lo salva in MongoDB

Il middleware supporta discovery dinamica delle variabili OPC UA sotto il nodo configurato.  
Questo riduce la dipendenza da un mapping hardcoded delle variabili.

### 5.2 Flusso documenti -> HDFS + MongoDB

Passi:

1. un client chiama `POST /api/documents/upload`
2. `DocumentsController` salva i file in una directory temporanea
3. costruisce una lista di `FileArtifactUploadRequest`
4. chiama `FileUploadService`
5. `FileUploadService` usa:
   - `WebHdfsFileStorageAdapter` per caricare il file in HDFS
   - `MongoFileArtifactMetadataRepository` per salvare i metadata
6. i file vengono organizzati in HDFS per `use case` e `category`
7. i metadata diventano interrogabili via API

Questa separazione e' intenzionale:

- HDFS memorizza il contenuto
- MongoDB memorizza il catalogo

### 5.3 Flusso documenti -> ricerca / download / delete

I documenti non vengono cercati direttamente su HDFS.  
La ricerca avviene sui metadata in MongoDB.

Questo rende semplici e veloci:

- filtro per nome
- filtro per use case
- filtro per categoria
- filtro per utente
- download singolo
- download multiplo in zip
- delete metadata + file HDFS

### 5.4 Flusso comandi Polaris

Passi tipici:

1. il client chiama un endpoint `/api/polaris/*`
2. `PolarisController` inoltra al `PolarisGCodeService` o `PolarisInfoService`
3. il service usa `PolarisGCodeAdapter`
4. l'adapter parla con il controller Polaris tramite la libreria `PolarisCommunication`

Comandi attualmente supportati:

- leggere info controller
- leggere stato G-code
- run di un file remoto
- upload-and-run
- enable di un asse

### 5.5 Flusso design -> job -> ProcessCoordinator

Questo e' il flusso nuovo che collega HMI, storage e macchina.

Passi:

1. l'utente configura uno use case dall'HMI
2. l'HMI salva un `ProductionDesign` via `POST /api/production/designs`
3. il design contiene:
   - identificativo univoco
   - use case
   - prodotto/individual selezionato
   - snapshot dei parametri per ogni step
   - recipe selezionata per ogni step
   - collegamento ai documenti part program su HDFS/Mongo
4. quando l'utente decide di produrre, l'HMI crea un `ProductionJob`
5. il `ProcessCoordinator` pesca il primo job `Queued`
6. recupera il design da MongoDB
7. scarica i part program collegati dal repository documentale
8. li carica su Polaris e li esegue in sequenza
9. aggiorna il job in MongoDB come:
   - `Validating`
   - `Running`
   - `Completed`
   - `Failed`
10. se si verifica un errore, il job va in `Failed` e il flusso si ferma

Questa strategia e' volutamente semplice:

- nessun retry automatico
- nessun recovery automatico
- restart/manual intervention richiesta dopo un fail

Il vantaggio e' che tutto il contesto rimane persistito e recuperabile:

- design
- file collegati
- job
- eventi di esecuzione

### 5.6 Flusso diagnostica

`MiddlewareDiagnosticsService` verifica la raggiungibilita' di:

- MongoDB
- WebHDFS NameNode
- WebHDFS DataNode
- OPC UA
- Polaris

Il risultato viene esposto via:

- `GET /api/system/status`

Inoltre gli stessi controlli vengono riutilizzati anche nei log di startup.

## 6. API principali

### Documenti

- `GET /api/documents`
- `GET /api/documents/{documentId}`
- `GET /api/documents/{documentId}/download`
- `DELETE /api/documents/{documentId}`
- `POST /api/documents/upload`
- `POST /api/documents/download-zip`

### Telemetria

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

### Produzione

- `GET /api/production/designs`
- `GET /api/production/designs/{designId}`
- `POST /api/production/designs`
- `GET /api/production/jobs`
- `GET /api/production/jobs/{jobId}`
- `POST /api/production/jobs`

### Diagnostica

- `GET /api/system/status`

## 7. Comandi console del Worker

Quando il `Middleware.Worker` e' in esecuzione, espone comandi da terminale utili per test e operazioni manuali.

Comandi attuali:

- `upload-files <username>`
- `polaris-run <localFileNameOrPath> [remoteFileName]`
- `polaris-run-remote <remoteFileName>`
- `polaris-enable-axis <axisName>`
- `polaris-gcode-status`
- `polaris-info`

Questa modalita' e' pensata soprattutto per:

- test veloci
- debug locale
- validazione delle integrazioni senza HMI

## 8. Configurazione

La configurazione condivisa si trova nel file:

- [`appsettings.json`](c:\Users\carlo\source\repos\WISE_Integrated_Middleware\appsettings.json)

Sia `Middleware.Api` sia `Middleware.Worker` leggono lo stesso file.

Sezioni principali:

- `Middleware.Mongo`
- `Middleware.OpcUa`
- `Middleware.Storage`
- `Middleware.Polaris`
- `Middleware.ProcessCoordinator`
- `Middleware.Worker`

### Configurazione Mongo

- `ConnectionString`
- `Database`
- `PlcTelemetryCollection`
- `FileMetadataCollection`
- `ProductionDesignCollection`
- `ProductionJobCollection`

### Configurazione OPC UA

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

### Configurazione Storage

- `UploadFolderPath`
- `DownloadFolderPath`
- `WebHdfsBaseUrl`
- `DataNodeWebHdfsBaseUrl`
- `NameNodeRpcAddress`
- `HdfsFolderPath`
- `HdfsUser`

### Configurazione Polaris

- `Enabled`
- `IpAddress`
- `GCodeDirectory`
- `LocalGCodeFolderPath`

### Configurazione Worker

- `PollingIntervalMs`

### Configurazione ProcessCoordinator

- `IdleDelayMs`
- `GCodeStatusPollingIntervalMs`
- `StepCompletionTimeoutMs`

## 9. Persistenza dati

### Telemetria PLC

Collection:

- `integration_plc_telemetry`

Schema envelope:

- `record_type`
- `source_type`
- `source_id`
- `timestamp_utc`
- `ingested_at_utc`
- `payload`

Il `payload` oggi contiene un dizionario di variabili lette via OPC UA.

### Metadata documenti

Collection:

- `integration_file_artifacts`

Contiene:

- `document_id`
- nome file
- nome originale
- path HDFS
- dimensione
- user
- mime type
- file type
- use case
- category
- source system

### File documentali

I contenuti binari vengono salvati in HDFS, non in MongoDB.

La struttura HDFS e' del tipo:

- `/WISE/{UseCase}/{Category}/{DocumentId}_{OriginalFileName}`

### Design di produzione

Collection:

- `production_designs`

Contiene:

- `design_id`
- `design_name`
- `use_case`
- `product_identifier`
- `designed_by`
- `designed_at_utc`
- `notes`
- `related_documents`
- `process_steps`

Ogni `process_step` persiste:

- sequenza
- nome processo
- label
- flag `requires_machine_execution`
- recipe selezionata
- snapshot parametri
- `part_program_document_id`
- nome file part program

### Job di produzione

Collection:

- `production_jobs`

Contiene:

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

## 10. Stato attuale della robustezza

Il middleware oggi e' gia' utile e funzionante, ma va considerato come base solida di laboratorio/integrazione avanzata, non ancora come piattaforma industriale definitiva.

Punti forti:

- layering chiaro
- configurazione unificata
- buona separazione tra metadata e file content
- integrazione Polaris gia' utilizzabile
- diagnostica di sistema presente
- orchestrazione produzione persistita con design e job separati

Limiti attuali:

- sicurezza API minima
- test automatici ancora limitati
- alcune API OPC UA usano metodi deprecati della libreria
- logging migliorabile con correlation id e metriche
- nessuna retention policy esplicita per la telemetria

## 11. Come partire se sei nuovo sul progetto

Se una persona nuova prende in mano il progetto, l'ordine corretto per capirlo e usarlo e' questo.

### Passo 1: capire i due host

Ci sono due eseguibili importanti:

- `Middleware.Api`
- `Middleware.Worker`

L'API serve ai client HTTP.  
Il Worker serve al polling e ai comandi console.

### Passo 2: leggere la configurazione

Apri:

- [`appsettings.json`](c:\Users\carlo\source\repos\WISE_Integrated_Middleware\appsettings.json)

e verifica:

- endpoint OPC UA
- config MongoDB
- config HDFS
- config Polaris
- polling worker

### Passo 3: capire i layer

Per orientarti:

- `Domain`: cosa sono i dati
- `Application`: quali casi d'uso esistono
- `Infrastructure`: come si parla con i sistemi esterni
- `Api`: come i client usano il middleware
- `Worker`: cosa succede in background

### Passo 4: avviare i componenti principali

Per usare il middleware in locale, servono almeno:

1. MongoDB attivo
2. HDFS/WebHDFS attivo se vuoi usare i documenti
3. server OPC UA attivo se vuoi la telemetria
4. Polaris attivo se vuoi i comandi macchina
5. `ProcessCoordinator` attivo se vuoi eseguire i job di produzione persistiti

Poi:

```powershell
dotnet run --project .\src\Middleware.Api
```

e/o

```powershell
dotnet run --project .\src\Middleware.Worker
```

e, per l'orchestrazione produzione:

```powershell
dotnet run --project .\src\ProcessCoordinator
```

### Passo 5: capire i flussi prima di modificare codice

Prima di cambiare qualcosa:

- per il PLC, guarda `MiddlewareIngestionService` + `OpcUaAdapter`
- per i documenti, guarda `FileUploadService` + `WebHdfsFileStorageAdapter` + repository Mongo
- per Polaris, guarda `PolarisController` + `PolarisGCodeService` + `PolarisGCodeAdapter`
- per la produzione, guarda `ProductionController` + repository `production_*` + `ProcessCoordinator`

Questo evita di toccare il layer sbagliato.

## 12. File chiave da conoscere

Se devi diventare autonomo, questi sono i file da leggere per primi:

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

## 13. In sintesi

Se dovessi spiegare il progetto a una persona nuova in poche righe:

- il `Worker` raccoglie dati dal PLC e offre comandi manuali
- l'`Api` espone servizi REST per client e HMI
- il `ProcessCoordinator` esegue i workflow di produzione persistiti
- `Infrastructure` integra OPC UA, Mongo, HDFS e Polaris
- `Application` orchestra i casi d'uso
- `Domain` definisce i dati condivisi

Il middleware e' quindi il punto unico di integrazione tra mondo macchina, storage e applicazioni client.
