namespace Pos.Web.Services;

/// <summary>Reglas de los codigos de barras.</summary>
public static class BarcodeRules
{
    /// <summary>
    /// Un EAN-13 tiene 13 digitos y el ultimo es un verificador calculado con los
    /// otros 12. Si no coincide, al imprimirlo como EAN-13 saldria otro numero y la
    /// pistola leeria un codigo distinto del guardado.
    /// </summary>
    public static bool IsValidEan13(string? code)
    {
        if (code is null || code.Length != 13 || !code.All(char.IsAsciiDigit)) return false;

        var sum = 0;
        for (var i = 0; i < 12; i++) sum += (code[i] - '0') * (i % 2 == 0 ? 1 : 3);
        return (10 - sum % 10) % 10 == code[12] - '0';
    }
}
