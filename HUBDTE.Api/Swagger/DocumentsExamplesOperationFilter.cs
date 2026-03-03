using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace HUBDTE.Api.Swagger;

public sealed class DocumentsExamplesOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var path = context.ApiDescription.RelativePath?.Trim('/');
        if (!string.Equals(path, "documents", StringComparison.OrdinalIgnoreCase))
            return;

        if (!string.Equals(context.ApiDescription.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
            return;

        if (operation.RequestBody?.Content is null) return;
        if (!operation.RequestBody.Content.TryGetValue("application/json", out var mediaType))
            return;

        mediaType.Examples ??= new Dictionary<string, OpenApiExample>();

        mediaType.Examples["DTE33"] = new OpenApiExample
        {
            Summary = "DTE 33",
            Description = "Factura (33). Incluye detalle, referencias y recargo (se imprime solo si el layout lo habilita).",
            Value = OpenApiAnyFactory.CreateFromJson(@"
            {
                ""source"": {
                    ""system"": ""SAP_RISE"",
                    ""company"": {
                        ""filialCode"": ""TTI"",
                        ""RutEmisor"": ""77857893"",
                        ""dvEmisor"": ""k"",
                        ""razon"": ""Tattersall Industrial S.A."",
                        ""giro"": ""Ventas y Representaciones de Equipos y Maquinarias"",
                        ""matriz"": ""Av. Presidente Frei Montalva 9829  Quilicura SANTIAGO CHILE"",
                        ""fono"": """",
                        ""fax"": """",
                        ""casilla"": """",
                        ""url"": ""http://www.tattersall-maquinarias.cl/"",
                        ""correo"": ""notificaciones.TTM@tattersall.cl"",
                        ""direccion"": ""Avda. Pdte. Edo. Frei Montalva 9829"",
                        ""comuna"": ""Quilicura"",
                        ""ciudad"": ""Santiago"",
                        ""sucursal"": { ""nombre"": """", ""sucSII"": """", ""dvEmisor"": """", ""direccion"": """", ""comuna"": """", ""ciudad"": """" }
                    }
                },
                ""document"": {
                    ""tipoDte"": 33,
                    ""folio"": 182,
                    ""docEntry"": 297,
                    ""fechaEmision"": ""20260213"",
                    ""fechaVencimiento"": ""20260315"",
                    ""condicionPago"": {""tipo"": 1, ""dias"": 0 },
                    ""moneda"": ""CLP"",
                    ""bodega"": ""S01A0401"",
                    ""template"": """",
                    ""vendedor"": {  ""codigo"": 1, ""nombre"": 0 }
                },
                ""receptor"": {
                    ""rut"": ""77501170"",
                    ""dv"": ""k"",
                    ""cardCode"": ""77501170"",
                    ""razonSocial"": ""SOC DE INVERSIONES EL MEMBRILLO LIMITADA"",
                    ""giro"": ""EXTRACCION DE MADERA"",
                    ""contacto"": ""Juan Perez"",
                    ""direccion"": ""HIJUELA 1  HACIENDA NILAHUE  S/N"",
                    ""comuna"": ""PUMANQUE"",
                    ""ciudad"": ""PUMANQUE"",
                    ""email"": ""jperez@gmail.com""
                },
                ""despacho"": {
                    ""direccion"": ""HIJUELA 1  HACIENDA NILAHUE  S/N"",
                    ""comuna"": ""PUMANQUE"",
                    ""ciudad"": ""PUMANQUE"",
                    ""patente"": """",
                    ""rutConductor"": """",
                    ""dvConductor"": """"
                },
                ""totales"": {
                    ""montoNeto"": 500680,
                    ""montoExento"": 0,
                    ""tasaIva"": 19,
                    ""iva"": 95129,
                    ""recargo"": 20000,
                    ""total"": 595809,
                    ""totalPalabras"": ""QUINIENTOS NOVENTA Y CINCO  MIL OCHOCIENTOS NUEVE PESOS"",
                    ""dolar"": 0,
                    ""totalDolares"": 0
                },
                ""referencias"": [
                    {
                    ""linea"": 1,
                    ""tipoDocRef"": 33,
                    ""folioRef"": 58962,
                    ""fechaRef"": ""20260315"",
                    ""codigoRef"": ""1"",
                    ""razonRef"": ""Maquina en mal estado""
                    }
                ],
                ""detalle"": [
                    {
                        ""linea"": 1,
                        ""tipoCodigo"": ""001"",
                        ""itemcode"": ""A07_REPPE01"",
                        ""DESCRIPCION"": ""REPARACION SEGUN PRESUPUESTO PL000136"",
                        ""cantidad"": 1,
                        ""precioUnitario"": 500680,
                        ""montoItem"": 500680,
                        ""afectoIva"": 0,
                        ""observacion"": ""A07_REPPE01"",
                        ""factExe"": { 
                            ""comentario"": 1, 
                            ""glosa"": 0 
                        }
                   },
                   {
                        ""linea"": 1,
                        ""tipoCodigo"": ""002"",
                        ""itemcode"": ""A07_REPPE02"",
                        ""DESCRIPCION"": ""REPARACION SEGUN PRESUPUESTO PL000136"",
                        ""cantidad"": 1,
                        ""precioUnitario"": 500680,
                        ""montoItem"": 500680,
                        ""afectoIva"": 0,
                        ""observacion"": ""A07_REPPE01"",
                        ""factExe"": { 
                            ""comentario"": 1, 
                            ""glosa"": 0 
                        }
                   }
              ],
              ""customFields"": [
                { ""grupo"": ""G"", ""codigo"": ""23"", ""nombre"": ""GPESOTOTAL"", ""valor"": 10 }
              ]
            }")
        };

        mediaType.Examples["DTE39_TTI"] = new OpenApiExample
        {
            Summary = "DTE 39 - Empresa TTI",
            Description = "Boleta (39). Incluye detalle, referencias y recargo (se imprime solo si el layout lo habilita).",
            Value = OpenApiAnyFactory.CreateFromJson(@"
            {
              ""source"": { ""company"": { ""filialCode"": ""TTI"" } },
              ""document"": { ""docEntry"": 898276, ""tipoDte"": 33 },
              ""totales"": { ""recargo"": 1500 },
              ""referencias"": [
                { ""linea"": 1, ""tipoDocRef"": 33, ""folioRef"": 58962, ""fechaRef"": ""20260315"" }
              ],
              ""detalle"": [
                { ""linea"": 1, ""tipoCodigo"": ""INT"", ""itemcode"": ""A001"", ""descripcion"": ""Producto 1"", ""cantidad"": 2, ""precio"": 1000 }
              ]
            }")
        };

        mediaType.Examples["DTE61_TTI_RecargoIgnorado"] = new OpenApiExample
        {
            Summary = "DTE 61 - Empresa TTI (recargo puede venir pero layout decide)",
            Description = "Nota de crédito (61). Aunque venga recargo, se imprime SOLO si el layout lo habilita para ese tipo/empresa.",
            Value = OpenApiAnyFactory.CreateFromJson(@"
            {
              ""source"": { ""company"": { ""filialCode"": ""TTI"" } },
              ""document"": { ""docEntry"": 777001, ""tipoDte"": 61 },
              ""totales"": { ""recargo"": 9999 },
              ""detalle"": [
                { ""linea"": 1, ""tipoCodigo"": ""INT"", ""itemcode"": ""NC01"", ""descripcion"": ""Ajuste"", ""cantidad"": 1, ""precio"": 500 }
              ]
            }")
        };

        mediaType.Examples["DTE52_TTMQ"] = new OpenApiExample
        {
            Summary = "DTE 52 - Empresa TTMQ",
            Description = "Guía de despacho (52). Glosas y reglas varían por tipo+empresa vía layout JSON.",
            Value = OpenApiAnyFactory.CreateFromJson(@"
            {
              ""source"": { ""company"": { ""filialCode"": ""TTMQ"" } },
              ""document"": { ""docEntry"": 123456, ""tipoDte"": 52 },
              ""totales"": { ""recargo"": 0 },
              ""detalle"": [
                { ""linea"": 1, ""tipoCodigo"": ""INT"", ""itemcode"": ""GD01"", ""descripcion"": ""Traslado"", ""cantidad"": 1, ""precio"": 0 }
              ]
            }")
        };

        operation.Summary = "Recibe documento SAP (JSON) y lo encola para generar TXT Azurian";
        operation.Description =
            "El body es un JSON flexible. La estructura exacta se define por layouts (base.json, tipo.{tipoDte}.json, tipo.{tipoDte}.emp.{empresa}.json).\n\n" +
            "Campos mínimos esperados:\n" +
            "- source.company.filialCode (string)\n" +
            "- document.docEntry (int/long)\n" +
            "- document.tipoDte (int)\n" +
            "- detalle[] (array)\n\n" +
            "Reglas especiales (por layout): recargo, glosas habilitadas, referencias, etc.";
    }
}