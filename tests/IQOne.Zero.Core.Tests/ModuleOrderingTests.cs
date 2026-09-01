using IQOne.Zero.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Tests;

/// Sira elle yazilmiyor; bagimliliklardan turetiliyor.
/// Bu testler o turetmenin dogrulugunu sabitliyor.
public class ModuleOrderingTests
{
    private static IReadOnlyList<IModule> Resolve(params IModule[] modules)
    {
        var services = new ServiceCollection();
        services.AddModules(modules);

        return services.BuildServiceProvider().GetRequiredService<IReadOnlyList<IModule>>();
    }

    [Fact]
    public void Bagimlilik_once_gelir()
    {
        var ordered = Resolve(new RadiologyModule(), new SharedModule(), new CoreModule());

        ordered.Select(m => m.Name).Should().Equal("CoreModule", "SharedModule", "RadiologyModule");
    }

    [Fact]
    public void Giris_sirasi_sonucu_degistirmez()
    {
        var a = Resolve(new CoreModule(), new SharedModule(), new RadiologyModule());
        var b = Resolve(new RadiologyModule(), new CoreModule(), new SharedModule());

        a.Select(m => m.Name).Should().Equal(b.Select(m => m.Name));
    }

    [Fact]
    public void Kardes_moduller_ada_gore_deterministik_siralanir()
    {
        var ordered = Resolve(new RadiologyModule(), new LaboratoryModule(), new SharedModule(), new CoreModule());

        // Laboratory ve Radiology'nin ikisi de Shared'a bagli; aralarinda ada gore sira
        ordered.Select(m => m.Name).Should()
            .Equal("CoreModule", "SharedModule", "LaboratoryModule", "RadiologyModule");
    }

    [Fact]
    public void Dongu_anlasilir_hata_verir()
    {
        var act = () => Resolve(new CycleA(), new CycleB());

        act.Should().Throw<ModuleDependencyCycleException>()
            .Which.Cycle.Should().Contain("CycleA").And.Contain("CycleB");
    }

    [Fact]
    public void Cozulmus_sira_okunabilir_sekilde_yazdirilir()
    {
        var services = new ServiceCollection();
        services.AddModules(new RadiologyModule(), new SharedModule(), new CoreModule());

        var text = services.BuildServiceProvider().DescribeModuleGraph();

        text.Should().Contain("Modul sirasi")
            .And.Contain("1. CoreModule")
            .And.Contain("3. RadiologyModule");
    }
}
