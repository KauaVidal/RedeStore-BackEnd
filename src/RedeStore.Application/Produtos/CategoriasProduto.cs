using RedeStore.Domain.Entities;

namespace RedeStore.Application.Produtos;

/// <summary>Conversão entre <see cref="CategoriaProduto"/> e o valor usado na API (nome em minúsculas, ex.: "calcas").</summary>
public static class CategoriasProduto
{
    private static readonly Dictionary<string, CategoriaProduto> PorValor =
        Enum.GetValues<CategoriaProduto>().ToDictionary(ParaValor);

    public static IReadOnlyCollection<string> Valores { get; } = PorValor.Keys.ToArray();

    public static string ParaValor(CategoriaProduto categoria) => categoria.ToString().ToLowerInvariant();

    public static CategoriaProduto? Converter(string? valor) =>
        valor is not null && PorValor.TryGetValue(valor, out var categoria) ? categoria : null;

    public static bool EhValida(string? valor) => valor is not null && PorValor.ContainsKey(valor);

    public static string MensagemInvalida { get; } =
        $"Categoria deve ser uma de: {string.Join(", ", PorValor.Keys.Select(v => $"'{v}'"))}.";
}
