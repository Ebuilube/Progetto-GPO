using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

string percorso = Path.Combine(AppContext.BaseDirectory, "input.txt");
string[] righe = File.ReadAllLines(percorso);
int n = int.Parse(righe[0]);
var attivita = new List<Attivita>();

for (int i = 1; i <= n; i++)
{
    // "C [A,B] 15" diventa "C ", "A,B", " 15".
    string[] parti = righe[i].Split('[', ']');

    attivita.Add(new Attivita
    {
        Nome = parti[0].Trim(),
        Predecessori = parti[1].Split(',', StringSplitOptions.RemoveEmptyEntries
                                             | StringSplitOptions.TrimEntries),
        Durata = int.Parse(parti[2])
    });
}

var perNome = attivita.ToDictionary(a => a.Nome);
var ordine = new List<Attivita>();

// Primo passaggio: calcola ES ed EF.
while (ordine.Count < n)
{
    int giaCalcolate = ordine.Count;

    foreach (Attivita a in attivita)
    {
        if (a.Calcolata || a.Predecessori.Any(p => !perNome[p].Calcolata))
            continue;

        foreach (string p in a.Predecessori)
            a.ES = Math.Max(a.ES, perNome[p].EF);

        a.EF = a.ES + a.Durata;
        a.Calcolata = true;
        ordine.Add(a);
    }

    if (ordine.Count == giaCalcolate)
        throw new InvalidOperationException("C'è un ciclo tra le attività.");
}

int fineProgetto = attivita.Select(a => a.EF).DefaultIfEmpty(0).Max();

foreach (Attivita a in attivita)
    a.LF = fineProgetto;

// Secondo passaggio: calcola LS e aggiorna LF dei predecessori.
for (int i = ordine.Count - 1; i >= 0; i--)
{
    Attivita a = ordine[i];
    a.LS = a.LF - a.Durata;

    foreach (string p in a.Predecessori)
        perNome[p].LF = Math.Min(perNome[p].LF, a.LS);
}

List<string> cammino = CamminoCritico.Trova(ordine, perNome, fineProgetto);

// La pagina carica il file come script anche quando viene aperta in locale.
var datiGantt = new
{
    durataProgetto = fineProgetto,
    camminoCritico = cammino,
    attivita = ordine.Select(a => new
    {
        nome = a.Nome,
        predecessori = a.Predecessori,
        durata = a.Durata,
        es = a.ES,
        ef = a.EF,
        ls = a.LS,
        lf = a.LF
    })
};

string cartellaSito = args.Length > 0 ? args[0] : ".";
Directory.CreateDirectory(cartellaSito);
string fileGantt = Path.GetFullPath(Path.Combine(cartellaSito, "gantt-data.js"));
string json = JsonSerializer.Serialize(datiGantt, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(fileGantt, $"window.ganttData = {json};\n");
Console.WriteLine($"File per il sito: {fileGantt}");

Console.WriteLine("Attività |  ES  EF  LS  LF");
foreach (Attivita a in attivita)
    Console.WriteLine($"{a.Nome,-8} | {a.ES,3} {a.EF,3} {a.LS,3} {a.LF,3}");

Console.WriteLine(cammino.Count == 0
    ? "Cammino critico: nessuna attività."
    : $"Cammino critico: {string.Join(" -> ", cammino)}");

class Attivita
{
    public string Nome = "";
    public string[] Predecessori = Array.Empty<string>();
    public int Durata;
    public int ES, EF, LS, LF;
    public bool Calcolata;
}
