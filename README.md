# HUBDTE

**HUBDTE** es una solución .NET 8 orientada al procesamiento confiable y asincrónico de documentos tributarios electrónicos (DTE), desde su recepción en formato JSON hasta la generación de archivos TXT para Azurian y su envío al servicio SOAP correspondiente.

Este documento presenta una versión **corporativa** del README, pensada para:

* incorporación de nuevos desarrolladores
* documentación interna del proyecto
* soporte operativo y técnico
* entendimiento de arquitectura, flujo y responsabilidades

---

## Índice

1. [Visión general](#1-visión-general)
2. [Objetivo del sistema](#2-objetivo-del-sistema)
3. [Alcance funcional](#3-alcance-funcional)
4. [Arquitectura de la solución](#4-arquitectura-de-la-solución)
5. [Proyectos y responsabilidades](#5-proyectos-y-responsabilidades)
6. [Decisiones de diseño](#6-decisiones-de-diseño)
7. [Patrones utilizados](#7-patrones-utilizados)
8. [Flujo funcional end-to-end](#8-flujo-funcional-end-to-end)
9. [Modelo de mensajería con RabbitMQ](#9-modelo-de-mensajería-con-rabbitmq)
10. [Layouts Azurian y construcción del TXT](#10-layouts-azurian-y-construcción-del-txt)
11. [Configuración principal](#11-configuración-principal)
12. [Requisitos previos](#12-requisitos-previos)
13. [Puesta en marcha](#13-puesta-en-marcha)
14. [API de ingreso](#14-api-de-ingreso)
15. [Observabilidad y trazabilidad](#15-observabilidad-y-trazabilidad)
16. [Operación y soporte](#16-operación-y-soporte)
17. [Cómo extender la solución](#17-cómo-extender-la-solución)
18. [Troubleshooting](#18-troubleshooting)
19. [Resumen ejecutivo técnico](#19-resumen-ejecutivo-técnico)

---

## 1. Visión general

HUBDTE desacopla el ingreso de documentos desde SAP del procesamiento técnico requerido para su transformación y envío a Azurian.

En términos simples, la solución:

* recibe un documento JSON
* lo registra en base de datos
* genera un mensaje de outbox en la misma transacción
* publica ese mensaje a RabbitMQ
* procesa el documento en background
* genera un TXT fixed-width según el tipo DTE y la empresa
* envía el resultado al servicio SOAP de Azurian
* aplica retry, DLQ y reproceso cuando corresponde

La solución fue diseñada para privilegiar:

* **confiabilidad operativa**
* **consistencia transaccional**
* **trazabilidad**
* **desac acoplamiento** entre ingreso y procesamiento
* **extensibilidad** por tipo de documento

---

## 2. Objetivo del sistema

El objetivo principal de HUBDTE es procesar DTEs de manera robusta, evitando que fallas transitorias o dependencias externas impacten directamente la experiencia de ingreso del documento.

En lugar de realizar todo el procesamiento en línea dentro del endpoint HTTP, el sistema divide la responsabilidad en dos etapas:

1. **Registro confiable del documento**
2. **Procesamiento asincrónico controlado**

Esta decisión reduce riesgos de:

* pérdida de mensajes
* duplicación de procesamiento
* bloqueo de la API por servicios externos
* fallos no recuperables por dependencia de infraestructura

---

## 3. Alcance funcional

La solución cubre actualmente:

* recepción de documentos JSON desde SAP
* validación básica de estructura de ingreso
* persistencia del documento y del outbox
* publicación a RabbitMQ
* consumo por tipo de cola
* generación de TXT Azurian por `TipoDte`
* carga de layouts por tipo y empresa
* envío al servicio SOAP Azurian
* reintentos automáticos
* DLQ y reproceso manual/asistido

Tipos DTE actualmente soportados:

* 33
* 34
* 39
* 52
* 56
* 61
* 110
* 111
* 112

---

## 4. Arquitectura de la solución

La solución se organiza en una arquitectura por capas con responsabilidades separadas.

```text
HUBDTE.sln
│
├── HUBDTE.Api
├── HUBDTE.Application
├── HUBDTE.Domain
├── HUBDTE.Infrastructure
└── HUBDTE.WorkerHost
```

Esta separación permite:

* aislar reglas de negocio
* desacoplar infraestructura de la lógica de aplicación
* facilitar mantenimiento y pruebas
* reducir acoplamiento entre entrada HTTP, persistencia, mensajería y procesamiento

---

## 5. Proyectos y responsabilidades

### 5.1 HUBDTE.Api

Responsabilidades:

* exponer el endpoint HTTP `/documents`
* proteger el ingreso mediante `X-Client-Token`
* registrar Swagger y ejemplos de payload
* delegar la ingesta a Application

No debe:

* construir TXT
* publicar directamente a RabbitMQ como flujo principal de negocio
* contener reglas de procesamiento documental

### 5.2 HUBDTE.Application

Responsabilidades:

* casos de uso de ingesta
* procesamiento del documento
* contratos de persistencia
* contratos de mensajería
* modelos intermedios para layouts y rendering fixed-width

Es la capa que orquesta la lógica sin conocer detalles concretos de infraestructura.

### 5.3 HUBDTE.Domain

Responsabilidades:

* representar entidades de negocio
* definir estados y transiciones válidas
* encapsular comportamiento de `SapDocument` y `OutboxMessage`

### 5.4 HUBDTE.Infrastructure

Responsabilidades:

* EF Core y persistencia SQL Server
* repositorios
* unit of work
* topología y publicación RabbitMQ
* retry policy y helpers de headers
* construcción de TXT Azurian
* cliente SOAP de Azurian

### 5.5 HUBDTE.WorkerHost

Responsabilidades:

* inicializar colas, DLQ y retry queues
* publicar outbox a RabbitMQ
* consumir colas de documentos
* reprocesar mensajes desde DLQ
* validar layouts y salida local en entorno de desarrollo

### 5.6 Proyectos que se ejecutan

La solución se inicia actualmente con dos proyectos:

* `HUBDTE.Api`
* `HUBDTE.WorkerHost`

---

## 6. Decisiones de diseño

### 6.1 La API no procesa el documento en línea

Se decidió que la API solo registre el documento y cree el outbox. El procesamiento real ocurre en segundo plano.

**Beneficio:** mejor resiliencia y menor acoplamiento con RabbitMQ/Azurian.

### 6.2 Persistencia antes que publicación

Se utiliza outbox para garantizar que un documento aceptado por la API quede persistido antes de ser publicado.

**Beneficio:** evita pérdida de mensajes por fallas entre DB y broker.

### 6.3 Procesamiento por colas por tipo DTE

Cada tipo DTE se enruta a una cola específica.

**Beneficio:** mejor aislamiento operativo y facilidad de escalamiento/diagnóstico.

### 6.4 Layouts externos por JSON

La lógica de render del TXT no depende de strings hardcodeados por tipo, sino de layouts configurables.

**Beneficio:** mayor mantenibilidad y menor costo de cambio por ajustes documentales.

### 6.5 Reintentos gobernados por RabbitMQ

El tiempo entre reintentos se maneja mediante colas TTL y DLX.

**Beneficio:** se evita lógica compleja de espera en código y se aprovecha el broker.

---

## 7. Patrones utilizados

### 7.1 Arquitectura por capas

Separación entre API, Application, Domain, Infrastructure y WorkerHost.

### 7.2 Repository Pattern

Ejemplos:

* `ISapDocumentRepository`
* `IOutboxMessageRepository`

### 7.3 Unit of Work

`IUnitOfWork` coordina transacciones y persistencia.

### 7.4 Outbox Pattern

Permite registrar documento y mensaje en la misma transacción, delegando la publicación a un proceso dedicado.

### 7.5 Idempotencia

Se evita doble procesamiento mediante:

* llave única por documento
* control de estados
* claim atómico para procesamiento

### 7.6 Builder Pattern

Usado para construcción de TXT por tipo de documento.

### 7.7 Options Pattern

Usado para configuración tipada de RabbitMQ, colas, retry, Azurian y layouts.

---

## 8. Flujo funcional end-to-end

### 8.1 Ingreso

La API recibe un `POST /documents` con un JSON dinámico.

### 8.2 Validación e ingesta

`DocumentIngestionService`:

* extrae `filialCode`, `docEntry`, `tipoDte`
* resuelve la cola correspondiente
* busca si el documento ya existe
* registra `SapDocument`
* crea `OutboxMessage`
* confirma la transacción

### 8.3 Publicación Outbox → RabbitMQ

`OutboxPublisherHostedService`:

* rescata mensajes atascados
* reclama un batch de outbox
* resuelve routing key por `MessageType`
* publica con publisher confirms
* marca el outbox como `Published` o `Failed`

### 8.4 Consumo Rabbit → procesamiento

`RabbitConsumerWorker`:

* consume de la cola principal por tipo DTE
* interpreta headers como `x-attempt`
* invoca `IDocumentProcessor`
* ante error, publica a retry o DLQ

### 8.5 Generación de TXT y envío

`DocumentProcessor`:

* carga el documento desde DB
* reclama el documento para procesamiento
* genera el TXT con `IAzurianTxtBuilder`
* opcionalmente lo deja en disco
* llama a `IAzurianClient`
* actualiza estado final del documento

### 8.6 DLQ y reproceso

`RabbitDlqReprocessorWorker`:

* consume la DLQ
* deja el documento otra vez en `Pending`
* reinicia contador de intentos
* republica a la cola principal

---

## 9. Modelo de mensajería con RabbitMQ

### 9.1 Exchanges

* principal: `documents.exchange`
* DLQ: `documents.dlq.exchange`

### 9.2 Convención de nombres

* principal: `documents.dteXX.queue`
* DLQ: `documents.dteXX.dlq`
* retry: `documents.dteXX.retry.01`, `.02`, `.03`, etc.

### 9.3 Retry policy actual

```json
"RetryPolicy": {
  "MaxAttempts": 5,
  "DelaysSeconds": [10, 30, 120, 600],
  "JitterSeconds": 3
}
```

Interpretación:

* intento 1 → retry.01
* intento 2 → retry.02
* intento 3 → retry.03
* intento 4 → retry.04
* intento 5 → DLQ

### 9.4 Headers principales

Se utilizan headers como:

* `x-attempt`
* `x-message-type`
* `CorrelationId`

Esto permite trazabilidad y control del flujo entre reintentos.

---

## 10. Layouts Azurian y construcción del TXT

La generación del TXT usa layouts configurables por archivo JSON.

Orden de resolución:

1. `base.json`
2. `tipo.{tipoDte}.json`
3. `tipo.{tipoDte}.emp.{empresa}.json`

Esto permite:

* comportamiento base reutilizable
* override por tipo de documento
* override específico por empresa o filial

La construcción del TXT se soporta en:

* `AzurianTxtBuilder`
* `AzurianTipoDteFixedWidthBuilder`
* `BaseAzurianFixedWidthBuilder`
* `FixedWidthRenderer`
* `FieldMapValueProvider`
* `AzurianLayoutRepository`

---

## 11. Configuración principal

### 11.1 Base de datos

```json
"ConnectionStrings": {
  "SqlServer": "Server=SQLDESA02;Database=FTC_PRUEBAS_VARIAS;User Id=rentway;Password=...;TrustServerCertificate=True;"
}
```

### 11.2 RabbitMQ

```json
"RabbitMq": {
  "HostName": "localhost",
  "Port": "5672",
  "VirtualHost": "/",
  "UserName": "guest",
  "Password": "guest",
  "Exchange": "documents.exchange"
}
```

### 11.3 Colas

```json
"Queues": {
  "Dte39": "documents.dte39.queue",
  "Dte33": "documents.dte33.queue",
  "Dte34": "documents.dte34.queue",
  "Dte110": "documents.dte110.queue",
  "Dte61": "documents.dte61.queue",
  "Dte56": "documents.dte56.queue",
  "Dte111": "documents.dte111.queue",
  "Dte112": "documents.dte112.queue",
  "Dte52": "documents.dte52.queue"
}
```

### 11.4 Azurian SOAP

```json
"AzurianSoap": {
  "ApiKey": "...",
  "RutEmpresa": 90035000,
  "ResolucionSii": 0,
  "Soap12Endpoint": "https://..."
}
```

### 11.5 Desarrollo TXT

```json
"AzurianDev": {
  "ForceWriteTxt": true,
  "OutputPath": "C:\AzurianDev\"
}
```

### 11.6 Layouts

```json
"AzurianLayoutFiles": {
  "LayoutsPath": "AzurianLayouts",
  "Constants": {
    "IndicadorE": "E",
    "CincoCeros": "00000",
    "CaracterEspacio": " ",
    "CaracterCero": "0",
    "ResolucionSii": "0"
  }
}
```

### 11.7 Simulación de fallas

```json
"FailureSimulation": {
  "Enabled": false,
  "FailTipoDte": 33,
  "FailAlways": true
}
```

---

## 12. Requisitos previos

Para ejecutar la solución en desarrollo se requiere:

* .NET 8 SDK
* SQL Server accesible desde la cadena configurada
* RabbitMQ activo en `localhost:5672`
* acceso al endpoint SOAP de Azurian
* carpeta `AzurianLayouts` disponible en `HUBDTE.WorkerHost`
* carpeta local de salida si `ForceWriteTxt = true`

---

## 13. Puesta en marcha

### 13.1 Restaurar paquetes

```bash
dotnet restore
```

### 13.2 Aplicar migraciones EF

Ejemplo general:

```bash
dotnet ef database update --project HUBDTE.Infrastructure --startup-project HUBDTE.Api
```

También puede utilizarse `Update-Database` desde Visual Studio.

### 13.3 Verificar RabbitMQ

Confirmar:

* host `localhost`
* puerto `5672`
* usuario `guest`
* password `guest`

### 13.4 Verificar layouts

Debe existir la carpeta:

```text
HUBDTE.WorkerHost/AzurianLayouts
```

con archivos como:

* `base.json`
* `tipo.39.json`
* `tipo.52.json`
* `tipo.56.json`
* `tipo.61.json`
* `tipo.33.emp.TTMQ.json`
* `tipo.34.emp.TTMQ.json`
* etc.

### 13.5 Configurar proyectos de inicio

La solución debe iniciar:

* `HUBDTE.Api`
* `HUBDTE.WorkerHost`

### 13.6 Ejecutar

La API expone Swagger en:

* `https://localhost:7139/swagger`
* `http://localhost:5179/swagger`

---

## 14. API de ingreso

### Endpoint principal

```http
POST /documents
```

### Header de seguridad

```http
X-Client-Token: TU_TOKEN
```

### Estructura mínima esperada

El payload debe contener, al menos:

* `source.company.filialCode` o `empresa` o `filial`
* `document.docEntry`
* `document.tipoDte`
* `detalle[]`

### Configuración del token

En el API debe existir:

```json
"Security": {
  "ClientToken": "TU_TOKEN"
}
```

Si el valor no se configura, el middleware no exige token.

---

## 15. Observabilidad y trazabilidad

La solución incorpora trazabilidad mediante:

* `CorrelationId` en RabbitMQ
* `x-attempt` para control de reintentos
* `x-message-type` para clasificación de mensajes
* `ErrorReason` en `SapDocuments`
* `CorrelationId` y `MessageTypeHeader` en `OutboxMessages`
* logs estructurados por documento, cola e intento

Esto facilita:

* análisis de errores
* seguimiento operativo
* auditoría del procesamiento

---

## 16. Operación y soporte

### Estado del documento

`SapDocument` maneja estados como:

* `Pending`
* `Processing`
* `Processed`
* `Failed`

### Estado del outbox

`OutboxMessage` maneja estados como:

* `Pending`
* `Processing`
* `Published`
* `Failed`

### Servicios operativos clave

* `RabbitTopologyInitializerHostedService`
* `OutboxPublisherHostedService`
* `RabbitConsumerWorker`
* `RabbitDlqReprocessorWorker`

---

## 17. Cómo extender la solución

### Agregar un nuevo TipoDte

De forma general, el proceso consiste en:

1. Agregar la cola en `QueuesOptions` y configuración `Queues`
2. Incorporar el tipo en `Program.cs` del WorkerHost
3. Crear o ajustar los layouts JSON correspondientes
4. Verificar que `MessageRoutingResolver` conozca el `MessageType`
5. Validar la estructura del payload para ese nuevo tipo

### Agregar un override por empresa

Crear un archivo del tipo:

```text
tipo.{tipoDte}.emp.{empresa}.json
```

Ejemplo:

```text
tipo.33.emp.TTMQ.json
```

---

## 18. Troubleshooting

### RabbitMQ no disponible

Revisar:

* servicio Rabbit levantado
* host, puerto, usuario y password
* exchanges y colas

### Layout no encontrado

Revisar:

* carpeta `AzurianLayouts`
* nombre y ubicación de archivos
* `AzurianLayoutFiles:LayoutsPath`

### Token inválido o ausente

Revisar:

* header `X-Client-Token`
* valor configurado en `Security:ClientToken`

### Mensajes van a retry o DLQ

Revisar:

* `ErrorReason` del documento
* logs del consumer
* `x-attempt`
* estado del documento

### Outbox no publica

Revisar:

* conectividad con RabbitMQ
* estado de `OutboxMessages`
* rescate de stuck processing y stale locks

### SOAP Azurian falla

Revisar:

* `Soap12Endpoint`
* `ApiKey`
* conectividad al servicio
* contenido del TXT generado

---

## 19. Resumen ejecutivo técnico

HUBDTE es una solución orientada a procesamiento asincrónico confiable de DTEs, basada en:

* persistencia transaccional
* outbox pattern
* mensajería RabbitMQ
* retry y DLQ
* idempotencia
* layouts configurables por tipo y empresa
* integración SOAP con Azurian

Su principal fortaleza es desacoplar el ingreso del documento del procesamiento técnico, mejorando resiliencia, trazabilidad y capacidad operativa.

En términos de mantenimiento, la solución está preparada para:

* incorporar nuevos tipos DTE
* agregar overrides por empresa
* diagnosticar fallas mediante logs, estados y correlation ids
* soportar operación continua con mecanismos de rescate y reproceso
