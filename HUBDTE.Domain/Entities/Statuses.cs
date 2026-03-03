namespace HUBDTE.Domain.Entities
{
    /// <summary>
    /// Estados posibles de un documento SAP dentro del ciclo de procesamiento.
    /// </summary>
    public enum SapDocumentStatus : byte
    {
        /// <summary>
        /// Documento registrado pero aún no procesado.
        /// </summary>
        Pending = 0,

        /// <summary>
        /// Documento actualmente siendo procesado por el worker.
        /// </summary>
        Processing = 1,

        /// <summary>
        /// Documento procesado correctamente (TXT generado + Azurian OK).
        /// Estado terminal.
        /// </summary>
        Processed = 2,

        /// <summary>
        /// Documento falló en procesamiento.
        /// Puede volver a Pending para reproceso.
        /// </summary>
        Failed = 3
    }

    /// <summary>
    /// Estados del mensaje en la tabla Outbox.
    /// </summary>
    public enum OutboxStatus : byte
    {
        /// <summary>
        /// Mensaje creado pero aún no publicado a RabbitMQ.
        /// </summary>
        Pending = 0,

        /// <summary>
        /// Mensaje publicado correctamente.
        /// Estado terminal.
        /// </summary>
        Published = 1,

        /// <summary>
        /// Mensaje falló y alcanzó máximo de intentos.
        /// Estado terminal.
        /// </summary>
        Failed = 2,

        /// <summary>
        /// Mensaje tomado por el publisher y en proceso de envío.
        /// </summary>
        Processing = 3
    }
}