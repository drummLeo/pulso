using Pulso;

internal static class SingleInstanceChecks
{
    public static int Run()
    {
        var temp = Directory.CreateTempSubdirectory("pulso-linux-instancia-");
        try
        {
            var woken = new ManualResetEventSlim(false);
            using (var first = SingleInstanceGuard.TryAcquire(temp.FullName, () => woken.Set()))
            {
                Should(first != null, "A primeira instância deve adquirir o bloqueio exclusivo.");
                var second = SingleInstanceGuard.TryAcquire(temp.FullName, () => { });
                Should(second == null, "A segunda instância deve falhar ao adquirir e sinalizar a primeira.");
                Should(woken.Wait(TimeSpan.FromSeconds(3)), "A primeira instância deve receber o sinal de reabertura pelo socket.");
            }

            using var reopened = SingleInstanceGuard.TryAcquire(temp.FullName, () => { });
            Should(reopened != null, "Após encerrar a primeira instância, o bloqueio e o socket ficam livres.");

            Console.WriteLine("OK  Instância única no Linux verifica bloqueio, sinalização via Unix socket e liberação.");
            return 0;
        }
        finally
        {
            if (temp.Parent?.FullName.TrimEnd(Path.DirectorySeparatorChar) == Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)
                && temp.Name.StartsWith("pulso-linux-instancia-", StringComparison.Ordinal))
                temp.Delete(true);
        }
    }

    private static void Should(bool condition, string failure)
    {
        if (!condition) throw new InvalidOperationException(failure);
    }
}
