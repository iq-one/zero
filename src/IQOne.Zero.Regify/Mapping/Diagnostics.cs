using Microsoft.CodeAnalysis;

namespace IQOne.Zero.Regify.Mapping;

internal static class MappingDiagnostics
{
    private const string Category = "Regify.Mapping";

    public static readonly DiagnosticDescriptor SchemaUnreadable = new(
        "RGF020", "Sema tanimi okunamadi",
        "'{0}' sema dosyasi cozumlenemedi", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor EntityNotFound = new(
        "RGF021", "Sema tanimindaki entity bulunamadi",
        "Sema '{0}' entity'sini tanimliyor ama bu ada sahip bir tip derlemede yok",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor PropertyNotFound = new(
        "RGF022", "Sema tanimindaki ozellik entity'de yok",
        "'{2}' kolonuna eslenen '{1}' ozelligi '{0}' entity'sinde bulunamadi",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor DuplicateTable = new(
        "RGF023", "Ayni tablo iki entity'ye eslenmis",
        "'{0}' tablosu hem '{1}' hem '{2}' tarafindan kullaniliyor",
        Category, DiagnosticSeverity.Warning, true);

    public static readonly DiagnosticDescriptor UnknownProvider = new(
        "RGF024", "Bilinmeyen veri saglayicisi",
        "'{0}' saglayicisi icin mapping emitter'i tanimli degil",
        Category, DiagnosticSeverity.Error, true);
}
