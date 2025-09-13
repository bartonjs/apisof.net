using System.Text;
using Microsoft.Cci;
using Terrajobst.UsageCrawling.Tests.Infra;

namespace Terrajobst.UsageCrawling.Tests;

public class AssemblyCrawlerTests
{
    [Fact]
    public void EmptyAssembly()
    {
        const string source = "";

        var usages = new HashSet<string>
        {
        };

        Check(source, usages);
    }

    [Fact]
    public void StructsOnly()
    {
        const string source =
            """
            public struct Foo
            {
                public int X;
                public int Y;
            }
            """;

        var usages = new HashSet<string>
        {
            "T:System.ValueType",
        };

        Check(source, usages);
    }

    [Fact]
    public void Attributes_Assembly()
    {
        const string source =
            """
            using System.Reflection;
            [assembly: AssemblyMetadata("key", "value")]
            """;

        var usages = new HashSet<string>
        {
            "T:System.Reflection.AssemblyMetadataAttribute",
            "M:System.Reflection.AssemblyMetadataAttribute.#ctor(System.String,System.String)"
        };

        Check(source, usages);
    }

    [Fact]
    public void Attributes_Module()
    {
        const string source =
            """
            using System;
            [module: CLSCompliant(false)]
            """;

        var usages = new HashSet<string>
        {
            "T:System.CLSCompliantAttribute",
            "M:System.CLSCompliantAttribute.#ctor(System.Boolean)"
        };

        Check(source, usages);
    }

    [Fact]
    public void Attributes_Type()
    {
        const string source =
            """
            using System;
            [Obsolete(DiagnosticId = "x")]
            class Test { }
            """;

        var usages = new HashSet<string>
        {
            "M:System.Object.#ctor", // Base call
            "T:System.ObsoleteAttribute",
            "M:System.ObsoleteAttribute.#ctor",
            "P:System.ObsoleteAttribute.DiagnosticId"
        };

        Check(source, usages);
    }

    [Fact]
    public void Attributes_Constructor()
    {
        const string source =
            """
            using System;
            class Test {
                [Obsolete]
                Test() { }
            }
            """;

        var usages = new HashSet<string>
        {
            "M:System.Object.#ctor", // Base call
            "T:System.ObsoleteAttribute",
            "M:System.ObsoleteAttribute.#ctor"
        };

        Check(source, usages);
    }

    [Fact]
    public void Attributes_Method()
    {
        const string source =
            """
            using System;
            class Test {
                [Obsolete]
                void DoStuff() { }
            }
            """;

        var usages = new HashSet<string>
        {
            "M:System.Object.#ctor", // Base call
            "T:System.ObsoleteAttribute",
            "M:System.ObsoleteAttribute.#ctor"
        };

        Check(source, usages);
    }

    [Fact]
    public void Attributes_Field()
    {
        const string source =
            """
            using System;
            class Test {
                [Obsolete]
                int _member;
            }
            """;

        var usages = new HashSet<string>
        {
            "M:System.Object.#ctor", // Base call
            "T:System.ObsoleteAttribute",
            "M:System.ObsoleteAttribute.#ctor"
        };

        Check(source, usages);
    }

    [Fact]
    public void Attributes_Property()
    {
        const string source =
            """
            using System;
            class Test {
                [Obsolete]
                int P { get; set; }
            }
            """;

        var usages = new HashSet<string>
        {
            "M:System.Object.#ctor", // Base call
            "T:System.ObsoleteAttribute",
            "M:System.ObsoleteAttribute.#ctor"
        };

        Check(source, usages);
    }

    [Fact]
    public void Attributes_Event()
    {
        const string source =
            """
            using System;
            class Test {
                [Obsolete]
                event EventHandler E;
            }
            """;

        var usages = new HashSet<string>
        {
            "T:System.EventHandler",
            "M:System.Object.#ctor",
            "M:System.Delegate.Combine(System.Delegate,System.Delegate)", // Adder
            "M:System.Delegate.Remove(System.Delegate,System.Delegate)",  // Remover
            "T:System.ObsoleteAttribute",
            "M:System.ObsoleteAttribute.#ctor",
            "M:System.Threading.Interlocked.CompareExchange``1(``0@,``0,``0)",
            "T:System.Threading.Interlocked",
            "T:System.Delegate",
        };

        Check(source, usages);
    }

    [Fact]
    public void Type_BaseType()
    {
        const string source =
            """
            class Test { }
            """;

        var usages = new HashSet<string>
        {
            "M:System.Object.#ctor", // Base call
        };

        Check(source, usages);
    }

    [Fact]
    public void Type_InterfaceImplementation()
    {
        const string source =
            """
            using System.Collections;
            class Test : IEnumerable {
                public IEnumerator GetEnumerator() => null;
            }
            """;

        var usages = new HashSet<string>
        {
            "M:System.Object.#ctor", // Base call
            "T:System.Collections.IEnumerable",
            "T:System.Collections.IEnumerator",
        };

        Check(source, usages);
    }

    [Fact]
    public void Method()
    {
        const string source =
            """
            using System;
            class Test {
                public void M() {
                    int.Parse("x");
                }
            }
            """;

        var usages = new HashSet<string>
        {
            "M:System.Object.#ctor", // Base call
            "M:System.Int32.Parse(System.String)"
        };

        Check(source, usages);
    }

    [Fact]
    public void Property_Get()
    {
        const string source =
            """
            using System;
            class Test {
                public void M() {
                    var x = "x".Length;
                }
            }
            """;

        var usages = new HashSet<string>
        {
            "M:System.Object.#ctor", // Base call
            "T:System.String",
            "M:System.String.get_Length",
        };

        Check(source, usages);
    }

    [Fact]
    public void Property_Set()
    {
        const string source =
            """
            using System;
            class Test {
                public void M() {
                    new ObsoleteAttribute {
                        DiagnosticId = "x"
                    };
                }
            }
            """;

        var usages = new HashSet<string>
        {
            "M:System.Object.#ctor", // Base call
            "T:System.ObsoleteAttribute",
            "M:System.ObsoleteAttribute.#ctor",
            "M:System.ObsoleteAttribute.set_DiagnosticId(System.String)",
        };

        Check(source, usages);
    }

    [Fact]
    public void Event_Add()
    {
        const string source =
            """
            using System;
            static class Test {
                static void M() {
                    AppDomain.CurrentDomain.UnhandledException += Handler;
                }
                static void Handler(object args, UnhandledExceptionEventArgs e) {}
            }
            """;

        var usages = new HashSet<string>
        {
            "T:System.AppDomain",
            "M:System.AppDomain.get_CurrentDomain",
            "T:System.UnhandledExceptionEventHandler",
            "M:System.UnhandledExceptionEventHandler.#ctor(System.Object,System.IntPtr)",
            "M:System.AppDomain.add_UnhandledException(System.UnhandledExceptionEventHandler)",
            "T:System.UnhandledExceptionEventArgs",
        };

        Check(source, usages);
    }

    [Fact]
    public void Event_Remove()
    {
        const string source =
            """
            using System;
            static class Test {
                static void M() {
                    AppDomain.CurrentDomain.UnhandledException -= Handler;
                }
                static void Handler(object args, UnhandledExceptionEventArgs e) {}
            }
            """;

        var usages = new HashSet<string>
        {
            "T:System.AppDomain",
            "M:System.AppDomain.get_CurrentDomain",
            "T:System.UnhandledExceptionEventHandler",
            "M:System.UnhandledExceptionEventHandler.#ctor(System.Object,System.IntPtr)",
            "M:System.AppDomain.remove_UnhandledException(System.UnhandledExceptionEventHandler)",
            "T:System.UnhandledExceptionEventArgs"
        };

        Check(source, usages);
    }

    [Fact]
    public void Field_Read()
    {
        const string source =
            """
            using System.IO;
            static class Test {
                static void M() {
                    var x = Path.PathSeparator;
                }
            }
            """;

        var usages = new HashSet<string>
        {
            "T:System.IO.Path",
            "F:System.IO.Path.PathSeparator",
        };

        Check(source, usages);
    }

    [Fact]
    public void Field_Write()
    {
        const string source =
            """
            using System.Runtime.InteropServices;
            static class Test {
                static void M() {
                    new MarshalAsAttribute((short)0) { MarshalType = "" };
                }
            }
            """;

        var usages = new HashSet<string>
        {
            "T:System.Runtime.InteropServices.MarshalAsAttribute",
            "M:System.Runtime.InteropServices.MarshalAsAttribute.#ctor(System.Int16)",
            "F:System.Runtime.InteropServices.MarshalAsAttribute.MarshalType"
        };

        Check(source, usages);
    }

    [Fact]
    public void Type_Generic()
    {
        const string source =
            """
            using System;
            static class Test {
                static void M(Func<Action, Attribute> p) {}
            }
            """;

        var usages = new HashSet<string>
        {
            "T:System.Func`2",
            "T:System.Action",
            "T:System.Attribute",
        };

        Check(source, usages);
    }

    [Fact]
    public void Type_Array()
    {
        const string source =
            """
            using System;
            static class Test {
                static void M(Action[] p) {}
            }
            """;

        var usages = new HashSet<string>
        {
            "T:System.Action"
        };

        Check(source, usages);
    }

    [Fact]
    public void Delegate_Invoke()
    {
        const string source =
            """
            using System;
            static class Test {
                static void M(string[] p) {
                    Action<string> action = Console.WriteLine;
                    action("Hello, World");
                }
            }
            """;

        var usages = new HashSet<string>
        {
            "T:System.String",
            "T:System.Action`1",
            "M:System.Console.WriteLine(System.String)",
            "T:System.Console",
            "M:System.Action`1.#ctor(System.Object,System.IntPtr)",
            "M:System.Action`1.Invoke(`0)"
        };

        Check(source, usages);
    }

    [Fact]
    public void GenericMethod()
    {
        const string source =
            """
            using System;
            using System.Threading.Tasks;
            
            static class Test {
                static object M(int value) {
                    return Task.FromResult(value);
                }
            }
            """;

        var usages = new HashSet<string>
        {
            "T:System.Threading.Tasks.Task",
            "T:System.Threading.Tasks.Task`1",
            "M:System.Threading.Tasks.Task.FromResult``1(``0)",
        };

        Check(source, usages);
    }

    [Fact]
    public void InstanceMethod_GenericType()
    {
        const string source =
            """
            using System;
            using System.Collections.Generic;

            static class Test {
                static void M(Dictionary<Action, Attribute> value) {
                    value.Add(null, null);
                }
            }
            """;

        var usages = new HashSet<string>
        {
            "T:System.Action",
            "T:System.Attribute",
            "T:System.Collections.Generic.Dictionary`2",
            "M:System.Collections.Generic.Dictionary`2.Add(`0,`1)",
        };

        Check(source, usages);
    }

    // TODO: Pointers

    private static void Check(string source, IReadOnlySet<string> expectedResults)
    {
        var assembly = new AssemblyBuilder()
            .SetAssembly(source);

        using (var peReader = assembly.ToPEReader())
        {
            Check(new LibraryReader(peReader), expectedResults);
        }
    }

    private static void Check(LibraryReader libraryReader, IReadOnlySet<string> expectedResultsText)
    {
        var crawler = new AssemblyCrawler();
        crawler.Crawl(libraryReader);

        Check(expectedResultsText, crawler.GetResults().Data);
    }

    private static void Check(
        IReadOnlyCollection<string> expectedResults,
        IReadOnlySet<ApiKey> actualResults)
    {

        var messageBuilder = new StringBuilder();

        foreach (var key in expectedResults.Where(k => !actualResults.Contains(new ApiKey(k))))
        {
            if (AssemblyBuilder.IsAutoGenerated(key))
                continue;

            messageBuilder.AppendLine($"{key} was expected but is missing.");
        }

        foreach (var key in actualResults.Where(k => !expectedResults.Contains(k.DocumentationId)))
        {
            if (AssemblyBuilder.IsAutoGenerated(key.DocumentationId))
                continue;

            messageBuilder.AppendLine($"{key} was not expected.");
        }

        if (messageBuilder.Length > 0)
            throw new Exception(messageBuilder.ToString());
    }
}