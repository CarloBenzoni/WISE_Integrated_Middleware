namespace Middleware.Infrastructure.Services;

public static class RuntimeDiagnosticMessages
{
    public static string ForComponent(string component, string detail) => component switch
    {
        "MongoDB" when detail.Contains("ReplicaSetGhost", StringComparison.OrdinalIgnoreCase)
            => "MongoDB risponde, ma il replica set non e inizializzato: manca un PRIMARY.",
        "MongoDB" => "MongoDB non pronto. Verificare processo, indirizzo e PRIMARY del replica set.",
        "OPC UA" => "PLC non raggiungibile dal controllo di rete. Verificare collegamento e indirizzo; se la macchina e scollegata e atteso.",
        "Polaris" => "Diagnosi del controller non riuscita. Verificare collegamento e indirizzo; se la macchina e scollegata e atteso.",
        "ADS" => "Beckhoff IPC non raggiungibile tramite ADS. Verificare collegamento, indirizzo e router ADS; se la macchina e scollegata e atteso.",
        "WebHDFS NameNode" => "NameNode non pronto. Verificare Hadoop e l'indirizzo WebHDFS.",
        "WebHDFS DataNode" => "DataNode non raggiungibile. Verificare il processo Hadoop DataNode e la porta configurata.",
        _ => Short(detail)
    };

    public static string ForException(Exception exception)
    {
        if (exception.ToString().Contains("MongoDB", StringComparison.OrdinalIgnoreCase))
            return ForComponent("MongoDB", exception.Message);
        if (exception is System.Net.Sockets.SocketException)
            return "Connessione non riuscita: dispositivo o servizio non raggiungibile. Verificare rete e indirizzo configurato.";
        return Short(exception.GetBaseException().Message);
    }

    private static string Short(string message)
    {
        var line = message.Replace('\r', ' ').Replace('\n', ' ');
        return line.Length <= 180 ? line : line[..180] + "...";
    }
}
