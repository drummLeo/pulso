using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using Pulso;

internal static class DesktopFlow
{
    [DllImport("user32.dll")] private static extern bool IsIconic(nint h);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint h);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint h);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(nint h, int command);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint h, nint other, int x, int y, int w, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool PrintWindow(nint h, nint dc, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint h, out Rect rectangle);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }

    private static void Wait(Func<bool> check, string label, int seconds = 10)
    {
        var time = Stopwatch.StartNew();
        while (time.Elapsed.TotalSeconds < seconds)
        {
            try { if (check()) return; }
            catch (ElementNotAvailableException) { }
            Thread.Sleep(85);
        }
        throw new InvalidOperationException(label);
    }
    private static AutomationElement? Find(AutomationElement parent, ControlType type, string name) =>
        parent.FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, type),
            new PropertyCondition(AutomationElement.NameProperty, name)));
    private static AutomationElement Element(AutomationElement root, ControlType type, string name)
    {
        AutomationElement? found = null;
        Wait(() => (found = Find(root, type, name)) != null, "Controle não encontrado: " + name);
        return found!;
    }
    private static void Click(AutomationElement root, string name)
    {
        var element = Element(root, ControlType.Button, name);
        ((InvokePattern)element.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
    }
    private static void Set(AutomationElement root, string name, string value)
    {
        var element = Element(root, ControlType.Edit, name);
        ((ValuePattern)element.GetCurrentPattern(ValuePattern.Pattern)).SetValue(value);
    }
    private static AutomationElement Dialog(AutomationElement root, int pid, string title)
    {
        AutomationElement? dialog = null;
        Wait(() =>
        {
            dialog = Find(root, ControlType.Window, title) ?? AutomationElement.RootElement.FindFirst(TreeScope.Children,
                new AndCondition(new PropertyCondition(AutomationElement.ProcessIdProperty, pid),
                    new PropertyCondition(AutomationElement.NameProperty, title)));
            return dialog != null;
        }, "Diálogo não encontrado: " + title);
        return dialog!;
    }
    private static void Snap(nint window, string output)
    {
        GetWindowRect(window, out var r);
        using var bitmap = new Bitmap(r.Right - r.Left, r.Bottom - r.Top);
        using var graphics = Graphics.FromImage(bitmap);
        var dc = graphics.GetHdc();
        bool ok;
        try { ok = PrintWindow(window, dc, 2); }
        finally { graphics.ReleaseHdc(dc); }
        if (!ok) throw new InvalidOperationException("Falhou a captura da janela de teste.");
        bitmap.Save(output, ImageFormat.Png);
    }
    private static nint Handle(Process p)
    {
        nint h = 0;
        string name = p.StartInfo.FileName;
        Wait(() =>
        {
            p.Refresh();
            if (p.HasExited) throw new InvalidOperationException(name + " saiu com código " + p.ExitCode);
            h = p.MainWindowHandle;
            return h != 0;
        }, "Janela não apareceu: " + name, 20);
        return h;
    }

    public static int CheckQuota(string executable)
    {
        var temp = Directory.CreateTempSubdirectory("pulso-uso-");
        var info = new ProcessStartInfo(Path.GetFullPath(executable)) { UseShellExecute = false };
        info.Environment["LOCALAPPDATA"] = temp.FullName;
        var store = new FocusStore(Path.Combine(temp.FullName, "PulsoFoco"));
        const string name = "Pulso.Verificacao";
        var now = DateTimeOffset.Now;
        // Keep the synthetic starting balance and the foreground observation in the same local hour.
        if (now.Minute == 59 && now.Second >= 25)
        {
            Thread.Sleep(TimeSpan.FromSeconds(61 - now.Second));
            now = DateTimeOffset.Now;
        }
        string key = UsageClock.AppKey(name);
        var data = UsageClock.Configure(new FocusData { Apps = [name] }, key, 1, null);
        data = UsageClock.Advance(UsageClock.Configure(data, null, null, 5), now, TimeZoneInfo.Local);
        var item = data.Usage.Items[key] with { HourTicks = TimeSpan.FromSeconds(57).Ticks };
        data.Usage.Items[key] = item;
        store.Save(data);
        var child = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        child.ArgumentList.Add("janela");
        nint previous = GetForegroundWindow();
        using var sample = new Process { StartInfo = child };
        using var desktop = new Process { StartInfo = info };
        try
        {
            desktop.Start();
            var root = AutomationElement.FromHandle(Handle(desktop));
            Element(root, ControlType.Button, "Iniciar sessão");
            sample.Start();
            nint target = Handle(sample);
            Thread.Sleep(1600);
            if (store.Load().Usage.Items[key].HourTicks != item.HourTicks) throw new InvalidOperationException("Janela em segundo plano consumiu a cota.");
            ShowWindowAsync(target, 9);
            SetForegroundWindow(target);
            if (GetForegroundWindow() != target) AutomationElement.FromHandle(target).SetFocus();
            Wait(() => GetForegroundWindow() == target, "A janela sintética não recebeu o foco do Windows.", 4);
            int foregroundChecks = 0, checks = 0;
            try
            {
                Wait(() => {
                    checks++;
                    if (GetForegroundWindow() == target) foregroundChecks++;
                    return IsIconic(target) && store.Load().Usage.Items[key].HourTicks >= TimeSpan.TicksPerMinute;
                }, "Janela em uso não foi minimizada ao atingir um minuto.", 15);
            }
            catch (InvalidOperationException)
            {
                var observed = store.Load().Usage;
                Console.Error.WriteLine($"Janela sintética em foco em {foregroundChecks}/{checks} amostras, minimizada={IsIconic(target)}, uso individual={TimeSpan.FromTicks(observed.Items[key].HourTicks)}, uso da lista={TimeSpan.FromTicks(observed.Shared.DayTicks)}, hora={observed.HourStart:HH:mm}.");
                throw;
            }
            var saved = store.Load();
            if (saved.Active != null || saved.Usage.Shared.DayTicks is <= 0 || saved.Usage.Shared.DayTicks > TimeSpan.FromSeconds(3).Ticks)
                throw new InvalidOperationException("Sessão ou saldo compartilhado incorretos no momento da pausa.");
            Click(root, "Lista de bloqueio");
            Click(root, "Editar limite de " + name);
            var edit = Dialog(root, desktop.Id, "Limite de " + name);
            Set(edit, "Minutos por hora (1 a 60)", "2");
            Click(edit, "Salvar limite");
            Wait(() => store.Load().Usage.Items[key].HourMinutes == 2, "Alteração da cota não foi salva.");
            ShowWindowAsync(target, 9);
            Thread.Sleep(1100);
            if (IsIconic(target) || store.Load().Usage.Items[key].HourTicks < TimeSpan.TicksPerMinute)
                throw new InvalidOperationException("Aumentar o limite não liberou a janela com o consumo preservado.");
            Console.WriteLine("OK  Janela real em primeiro plano esgotou a cota fora de sessão, foi minimizada e foi liberada ao editar o limite.");
            return 0;
        }
        finally
        {
            foreach (var p in new[] { sample, desktop })
                try { if (!p.HasExited) { p.Kill(); p.WaitForExit(4000); } } catch (InvalidOperationException) { }
            if (previous != 0) SetForegroundWindow(previous);
            if (temp.Parent?.FullName.TrimEnd('\\') == Path.GetTempPath().TrimEnd('\\') && temp.Name.StartsWith("pulso-uso-"))
                temp.Delete(true);
        }
    }
    public static int Run(string executable, string snapshot)
    {
        var temp = Directory.CreateTempSubdirectory("pulso-desktop-");
        var info = new ProcessStartInfo(Path.GetFullPath(executable)) { UseShellExecute = false, RedirectStandardError = true };
        info.Environment["LOCALAPPDATA"] = temp.FullName;
        info.Environment["DOTNET_DISABLE_GUI_ERRORS"] = "1";
        var store = new FocusStore(Path.Combine(temp.FullName, "PulsoFoco"));
        using var desktop = Process.Start(info)!;
        desktop.ErrorDataReceived += (_, e) => { if (e.Data != null) Console.Error.WriteLine(e.Data); };
        desktop.BeginErrorReadLine();
        var childInfo = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        childInfo.ArgumentList.Add("janela");
        using var sample = new Process { StartInfo = childInfo };
        try
        {
            nint handle = Handle(desktop);
            Console.WriteLine("Janela principal encontrada.");
            sample.Start();
            nint target = Handle(sample);
            var root = AutomationElement.FromHandle(handle);
            Element(root, ControlType.Button, "Iniciar sessão");
            Thread.Sleep(450);
            Snap(handle, snapshot);
            Console.WriteLine("OK  Executável publicado abriu a tela inicial");
            Click(root, "Lista de bloqueio");
            Set(root, "Endereço do site", "invalido");
            Click(root, "Adicionar site");
            Element(root, ControlType.Text, "Digite um domínio válido, como exemplo.com.");
            Set(root, "Endereço do site", "https://www.pulso.test/videos");
            Click(root, "Adicionar site");
            Wait(() => store.Load().Sites.SequenceEqual(["pulso.test"]), "Site não foi salvo pela interface.");
            Click(root, "Escolher janela aberta");
            var picker = Dialog(root, desktop.Id, "Escolher janela aberta");
            var item = Element(picker, ControlType.ListItem, "Pulso.Verificacao");
            ((SelectionItemPattern)item.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
            Click(picker, "Adicionar aplicativo");
            Wait(() => store.Load().Apps.Contains("Pulso.Verificacao"), "Aplicativo não foi salvo.");
            Console.WriteLine("OK  Lista aceita domínio e janela real e indica endereço inválido");
            Click(root, "Configurar limite compartilhado");
            var sharedLimit = Dialog(root, desktop.Id, "Limite compartilhado");
            Set(sharedLimit, "Minutos por hora (1 a 60)", "61");
            Set(sharedLimit, "Minutos por dia (1 a 1440)", "35");
            Click(sharedLimit, "Salvar limite");
            Set(sharedLimit, "Minutos por hora (1 a 60)", "10");
            Click(sharedLimit, "Salvar limite");
            Wait(() => store.Load().Usage.Shared is { HourMinutes: 10, DayMinutes: 35 }, "Limite compartilhado não salvo.");
            Click(root, "Configurar limite de pulso.test");
            var siteLimit = Dialog(root, desktop.Id, "Limite de pulso.test");
            Set(siteLimit, "Minutos por hora (1 a 60)", "5");
            Click(siteLimit, "Salvar limite");
            string siteKey = UsageClock.SiteKey("pulso.test");
            Wait(() => store.Load().Usage.Items.TryGetValue(siteKey, out var rule) && rule.HourMinutes == 5, "Limite individual não foi salvo.");
            Click(root, "Editar limite de pulso.test");
            siteLimit = Dialog(root, desktop.Id, "Limite de pulso.test");
            Set(siteLimit, "Minutos por hora (1 a 60)", "");
            Set(siteLimit, "Minutos por dia (1 a 1440)", "17");
            Click(siteLimit, "Salvar limite");
            Wait(() => store.Load().Usage.Items[siteKey] is { HourMinutes: null, DayMinutes: 17 }
                     && store.Load().Usage.Shared.HourMinutes == 10, "Edição de um limite interferiu em outro.");
            Snap(handle, Path.Combine(Path.GetDirectoryName(snapshot)!, "pulso-limites.png"));
            Console.WriteLine("OK  Limite individual e saldo compartilhado coexistem, validam minutos e aceitam edição");
            Click(root, "Agendamentos");
            Click(root, "Novo agendamento");
            var routine = Dialog(root, desktop.Id, "Novo agendamento");
            Set(routine, "Nome da sessão", "Leitura da tarde");
            Set(routine, "Horário de início (HH:mm)", DateTime.Now.AddHours(4).ToString("HH:mm"));
            Set(routine, "Duração em minutos (1 a 720)", "35");
            Click(routine, "Salvar agendamento");
            Wait(() => store.Load().Routines.SingleOrDefault()?.Minutes == 35, "Agendamento não salvo.");
            Click(root, "Desativar Leitura da tarde");
            Wait(() => !store.Load().Routines.Single().Enabled, "Agendamento não desativado.");
            Click(root, "Editar Leitura da tarde");
            routine = Dialog(root, desktop.Id, "Editar agendamento");
            Set(routine, "Nome da sessão", "Leitura editada");
            Click(routine, "Salvar agendamento");
            Wait(() => store.Load().Routines.Single().Name == "Leitura editada", "Edição não persistida.");
            Click(root, "Excluir Leitura editada");
            Click(Dialog(root, desktop.Id, "Excluir agendamento?"), "Excluir agendamento");
            Wait(() => store.Load().Routines.Length == 0, "Agendamento não excluído.");
            Console.WriteLine("OK  Criação, desativação, edição e exclusão de agendamento");
            Click(root, "Visão geral");
            Click(root, "Outro");
            var duration = Dialog(root, desktop.Id, "Duração da sessão");
            Set(duration, "Duração em minutos (1 a 720)", "18");
            Click(duration, "Usar duração");
            Wait(() => store.Load().Minutes == 18, "Duração personalizada não salva.");
            Click(root, "Iniciar sessão");
            Wait(() => store.Load().Active != null && IsIconic(target) && !sample.HasExited, "Janela de teste não foi minimizada.");
            ShowWindowAsync(target, 9);
            Thread.Sleep(1100);
            if (!IsIconic(target)) throw new InvalidOperationException("A janela voltou durante a sessão.");
            ((WindowPattern)root.GetCurrentPattern(WindowPattern.Pattern)).Close();
            Wait(() => !IsWindowVisible(handle) && !desktop.HasExited, "A janela não foi recolhida na bandeja.");
            using (var second = Process.Start(info)!) second.WaitForExit(5000);
            Wait(() => IsWindowVisible(handle) && !desktop.HasExited, "Segunda abertura não restaurou a janela.");
            root = AutomationElement.FromHandle(handle);
            Click(root, "Encerrar sessão");
            Click(Dialog(root, desktop.Id, "Encerrar a sessão?"), "Encerrar sessão");
            Wait(() => store.Load().Active == null && store.Load().History.Length == 1, "Sessão não foi encerrada.");
            ShowWindowAsync(target, 9);
            Thread.Sleep(1000);
            if (IsIconic(target)) throw new InvalidOperationException("A janela não foi liberada.");
            Console.WriteLine("OK  Janela bloqueada durante o foco, sessão na bandeja e liberação ao encerrar");
            Click(root, "Histórico");
            Element(root, ControlType.Text, "Foco rápido");
            Click(root, "Navegadores");
            Element(root, ControlType.Button, "Copiar caminho da extensão");
            Click(root, "Lista de bloqueio");
            Click(root, "Remover pulso.test");
            Click(root, "Remover Pulso.Verificacao");
            Wait(() => store.Load().Sites.Length + store.Load().Apps.Length == 0, "Remoção de itens falhou.");
            Click(root, "Visão geral");
            SetWindowPos(handle, 0, 0, 0, 690, 560, 0x0016);
            Thread.Sleep(400);
            Snap(handle, Path.Combine(Path.GetDirectoryName(snapshot)!, "pulso-compacto.png"));
            Console.WriteLine("OK  Histórico, instruções, remoção de itens e janela compacta");
            return 0;
        }
        finally
        {
            foreach (var process in new[] { sample, desktop })
                try { if (!process.HasExited) { process.Kill(); process.WaitForExit(4000); } }
                catch (InvalidOperationException) { }
            if (string.Equals(temp.Parent?.FullName.TrimEnd('\\'), Path.GetTempPath().TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                && temp.Name.StartsWith("pulso-desktop-", StringComparison.Ordinal)) temp.Delete(true);
        }
    }
}
