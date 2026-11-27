using System.Collections.Generic;
using System.Linq;

static class CamminoCritico
{
    public static List<string> Trova(List<Attivita> ordine,
        Dictionary<string, Attivita> perNome, int fineProgetto)
    {
        var cammino = new List<string>();
        Attivita corrente = ordine.LastOrDefault(a => a.EF == fineProgetto);

        while (corrente != null)
        {
            cammino.Add(corrente.Nome);

            // Risale a un predecessore che termina quando inizia l'attività corrente.
            int inizio = corrente.ES;
            corrente = corrente.Predecessori.Select(nome => perNome[nome])
                .FirstOrDefault(p => p.EF == inizio);
        }

        cammino.Reverse();
        return cammino;
    }
}
