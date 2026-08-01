using System.Reflection;
using FluentAssertions;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Mapping.Extensions;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class MapperNestedCompositionTests
{
    private static readonly string[] ExpectedSinglePushResult = ["push!"];
    private static readonly string[] ExpectedPullPressResults = ["pull!", "press!"];
    private static readonly string[] ExpectedBaseDerivedMarkers = ["base", "derived"];

    [Test]
    public void Map_Should_Support_Nested_Object_And_List_Mapping_With_Context_Propagation()
    {
        var mapper = CreateMapper(new NestedCompositionProfile());
        var context = mapper.CreateContext();
        context.Set(NestedCompositionProfile.Keys.Suffix, "!");

        var source = new ParentSource
        {
            Child = new ChildSource { Name = "bench" },
            Children =
            [
                new ChildSource { Name = "squat" },
                new ChildSource { Name = "deadlift" }
            ]
        };

        var result = mapper.Map<ParentSource, ParentTarget>(source, context);

        result.Child?.Name.Should().Be("bench!");
        result.Children.Select(c => c.Name).Should().Equal(["squat!", "deadlift!"]);
    }

    [Test]
    public void Map_Should_Support_Nested_Object_Mapping_With_Context_Propagation()
    {
        var mapper = CreateMapper(new NestedCompositionProfile());
        var context = mapper.CreateContext();
        context.Set(NestedCompositionProfile.Keys.Suffix, "!");

        var source = new ParentSource
        {
            Child = new ChildSource { Name = "press" }
        };

        var result = mapper.Map<ParentSource, ParentTarget>(source, context);

        result.Child?.Name.Should().Be("press!");
        result.Children.Should().BeEmpty();
    }

    [Test]
    public void Map_Should_Support_Nested_List_Mapping_With_Context_Propagation()
    {
        var mapper = CreateMapper(new NestedCompositionProfile());
        var context = mapper.CreateContext();
        context.Set(NestedCompositionProfile.Keys.Suffix, "!");

        var source = new ParentSource
        {
            Children =
            [
                new ChildSource { Name = "row" },
                new ChildSource { Name = "curl" }
            ]
        };

        var result = mapper.Map<ParentSource, ParentTarget>(source, context);

        result.Child.Should().BeNull();
        result.Children.Select(c => c.Name).Should().Equal(["row!", "curl!"]);
    }

    [Test]
    public void Map_With_Runtime_Source_Type_Should_Support_New_Single_Generic_Overload()
    {
        var mapper = CreateMapper(new NestedCompositionProfile());
        var context = mapper.CreateContext();
        context.Set(NestedCompositionProfile.Keys.Suffix, "!");

        object source = new ParentSource
        {
            Child = new ChildSource { Name = "pull" },
            Children =
            [
                new ChildSource { Name = "push" }
            ]
        };

        var result = mapper.Map<ParentTarget>(source, context);

        result.Child?.Name.Should().Be("pull!");
        result.Children.Select(c => c.Name).Should().Equal(ExpectedSinglePushResult);
    }

    [Test]
    public void Map_With_Runtime_Source_Type_Should_Select_Runtime_Mapping_Over_Compile_Time_Type()
    {
        var mapper = CreateMapper(new RuntimePolymorphismProfile());
        BaseSource source = new DerivedSource();

        var result = mapper.Map<PolymorphicTarget>(source);

        result.Marker.Should().Be("derived");
    }

    [Test]
    public void Map_With_Runtime_Source_Type_Should_Throw_Clear_Error_When_Mapping_Is_Missing()
    {
        var mapper = CreateMapper(new MissingNestedMappingProfile());
        object source = new MissingChildSource();

        var act = new Action(() => mapper.Map<MissingChildTarget>(source));
        var ex = act.Should().Throw<InvalidOperationException>().And;

        ex.Message.Should().Contain("Mapping from MissingChildSource to MissingChildTarget is not registered.");
    }

    [Test]
    public void MapList_With_Runtime_Source_Type_Should_Map_All_Items()
    {
        var mapper = CreateMapper(new NestedCompositionProfile());
        var context = mapper.CreateContext();
        context.Set(NestedCompositionProfile.Keys.Suffix, "!");

        System.Collections.IEnumerable source = new object[]
        {
            new ChildSource { Name = "pull" },
            new ChildSource { Name = "press" }
        };

        var result = mapper.MapList<ChildTarget>(source, context);

        result.Select(x => x.Name).Should().Equal(ExpectedPullPressResults);
    }

    [Test]
    public void MapList_With_Runtime_Source_Type_Should_Select_Runtime_Mapping_Per_Item()
    {
        var mapper = CreateMapper(new RuntimePolymorphismProfile());
        System.Collections.IEnumerable source = new BaseSource[]
        {
            new BaseSource(),
            new DerivedSource()
        };

        var result = mapper.MapList<PolymorphicTarget>(source);

        result.Select(x => x.Marker).Should().Equal(ExpectedBaseDerivedMarkers);
    }

    [Test]
    public void MapTo_Extension_Should_Use_Runtime_Source_Type()
    {
        var mapper = CreateMapper(new RuntimePolymorphismProfile());
        object source = new DerivedSource();

        var result = source.MapTo<PolymorphicTarget>(mapper);

        result.Marker.Should().Be("derived");
    }

    [Test]
    public void MapToList_Extension_Should_Use_Runtime_Source_Type_Per_Item()
    {
        var mapper = CreateMapper(new RuntimePolymorphismProfile());
        System.Collections.IEnumerable source = new BaseSource[]
        {
            new BaseSource(),
            new DerivedSource()
        };

        var result = source.MapToList<PolymorphicTarget>(mapper);

        result.Select(x => x.Marker).Should().Equal(ExpectedBaseDerivedMarkers);
    }

    [Test]
    public void Map_Should_Throw_Clear_Error_When_Nested_Mapping_Is_Missing()
    {
        var mapper = CreateMapper(new MissingNestedMappingProfile());
        var source = new MissingParentSource { Child = new MissingChildSource() };

        var act = new Action(() => mapper.Map<MissingParentSource, MissingParentTarget>(source));
        var ex = act.Should().Throw<InvalidOperationException>().And;

        ex.Message.Should().Contain("Mapping from MissingChildSource to MissingChildTarget is not registered.");
    }

    [Test]
    public void Map_Should_Throw_Clear_Error_When_Cyclic_Nested_Mapping_Is_Detected()
    {
        var mapper = CreateMapper(new CyclicNestedMappingProfile());
        var source = new CyclicSource { TriggerCycle = true };

        var act = new Action(() => mapper.Map<CyclicSource, CyclicTarget>(source));
        var ex = act.Should().Throw<InvalidOperationException>().And;

        ex.Message.Should().Contain("Cyclic nested mapping detected.");
    }

    [Test]
    public void Map_Should_Throw_Clear_Error_When_Cyclic_ValueType_Nested_Mapping_Is_Detected()
    {
        var mapper = CreateMapper(new CyclicValueTypeMappingProfile());

        var act = new Action(() => mapper.Map<int, int>(1));
        var ex = act.Should().Throw<InvalidOperationException>().And;

        ex.Message.Should().Contain("Cyclic nested mapping detected.");
    }

    [Test]
    public void Mapper_Should_Throw_When_Profiles_Register_A_Duplicate_Map()
    {
        var action = () => CreateMapper(new PlainIntMappingProfile(), new PlainIntMappingProfile());

        var exception = action.Should().Throw<TargetInvocationException>().Which;

        exception.InnerException.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Be("Mapping from Int32 to Int32 is already registered.");
    }

    [Test]
    public void Map_Should_Throw_When_Context_Is_Bound_To_Different_Mapper()
    {
        var firstMapper = CreateMapper(new NestedCompositionProfile());
        var secondMapper = CreateMapper(new NestedCompositionProfile());
        var context = firstMapper.CreateContext();

        var act = new Action(() =>
            secondMapper.Map<ParentSource, ParentTarget>(new ParentSource(), context));
        var ex = act.Should().Throw<InvalidOperationException>().And;

        ex.Message.Should().Contain("already bound to a different mapper");
    }

    [Test]
    public async Task Map_Should_Not_Leak_Cycle_State_Between_Parallel_Executions()
    {
        var mapper = CreateMapper(new ParallelValueTypeMappingProfile());
        var context = mapper.CreateContext();

        var first = Task.Run(() => mapper.Map<int, int>(1, context));
        var second = Task.Run(() => mapper.Map<int, int>(1, context));

        var results = await Task.WhenAll(first, second);

        results.Should().Equal([1, 1]);
    }

    [Test]
    public async Task Map_Should_Allow_Only_One_Mapper_To_Bind_Unbound_Context_Concurrently()
    {
        var firstMapper = CreateMapper(new PlainIntMappingProfile());
        var secondMapper = CreateMapper(new PlainIntMappingProfile());
        var context = new MappingContext();

        var firstTask = Task.Run(() => TryMap(firstMapper, context));
        var secondTask = Task.Run(() => TryMap(secondMapper, context));

        var results = await Task.WhenAll(firstTask, secondTask);

        results.Count(r => r.Success).Should().Be(1);
        results.Count(r => !r.Success).Should().Be(1);
        results.Single(r => !r.Success).Exception.Should().BeOfType<InvalidOperationException>();
    }

    private static IMapper CreateMapper(params IMappingProfile[] profiles)
    {
        return (IMapper)Activator.CreateInstance(
            typeof(Mapper),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [profiles],
            culture: null)!;
    }

    private sealed class NestedCompositionProfile : IMappingProfile
    {
        internal static class Keys
        {
            internal static readonly ContextKey<string> Suffix = new("Tests.Suffix");
        }

        public void Configure(MappingConfiguration configuration)
        {
            configuration.AllowContextKey(Keys.Suffix);

            configuration.CreateMap<ChildSource, ChildTarget>((source, context) =>
            {
                var suffix = context?.Get(Keys.Suffix) ?? string.Empty;
                return new ChildTarget { Name = source.Name + suffix };
            });

            configuration.CreateMap<ParentSource, ParentTarget>((source, context) => new ParentTarget
            {
                Child = source.Child == null ? null : context!.Map<ChildTarget>(source.Child),
                Children = context!.MapList<ChildTarget>(source.Children)
            });
        }
    }

    private sealed class MissingNestedMappingProfile : IMappingProfile
    {
        public void Configure(MappingConfiguration configuration)
        {
            configuration.CreateMap<MissingParentSource, MissingParentTarget>((source, context) => new MissingParentTarget
            {
                Child = source.Child == null ? null : context!.Map<MissingChildTarget>(source.Child)
            });
        }
    }

    private sealed class CyclicNestedMappingProfile : IMappingProfile
    {
        public void Configure(MappingConfiguration configuration)
        {
            configuration.CreateMap<CyclicSource, CyclicTarget>((source, context) =>
            {
                if (source.TriggerCycle)
                {
                    return context!.Map<CyclicTarget>(source);
                }

                return new CyclicTarget();
            });
        }
    }

    private sealed class CyclicValueTypeMappingProfile : IMappingProfile
    {
        public void Configure(MappingConfiguration configuration)
        {
            configuration.CreateMap<int, int>((source, context) =>
            {
                if (source == 1)
                {
                    return context!.Map<int>(source);
                }

                return source;
            });
        }
    }

    private sealed class ParallelValueTypeMappingProfile : IMappingProfile
    {
        private readonly Barrier _barrier = new(participantCount: 2);

        public void Configure(MappingConfiguration configuration)
        {
            configuration.CreateMap<int, int>((source, _) =>
            {
                _barrier.SignalAndWait(TimeSpan.FromSeconds(30));

                return source;
            });
        }
    }

    private sealed class PlainIntMappingProfile : IMappingProfile
    {
        public void Configure(MappingConfiguration configuration)
        {
            configuration.CreateMap<int, int>((source, _) => source);
        }
    }

    private sealed class RuntimePolymorphismProfile : IMappingProfile
    {
        public void Configure(MappingConfiguration configuration)
        {
            configuration.CreateMap<BaseSource, PolymorphicTarget>((_, _) => new PolymorphicTarget { Marker = "base" });
            configuration.CreateMap<DerivedSource, PolymorphicTarget>((_, _) => new PolymorphicTarget { Marker = "derived" });
        }
    }

    private static MapAttemptResult TryMap(IMapper mapper, MappingContext context)
    {
        try
        {
            _ = mapper.Map<int, int>(1, context);
            return new MapAttemptResult(Success: true, Exception: null);
        }
        catch (Exception ex)
        {
            return new MapAttemptResult(Success: false, Exception: ex);
        }
    }

    private readonly record struct MapAttemptResult(bool Success, Exception? Exception);

    private sealed class ParentSource
    {
        public ChildSource? Child { get; init; }
        public List<ChildSource> Children { get; init; } = [];
    }

    private sealed class ChildSource
    {
        public string Name { get; init; } = string.Empty;
    }

    private sealed class ParentTarget
    {
        public ChildTarget? Child { get; init; }
        public List<ChildTarget> Children { get; init; } = [];
    }

    private sealed class ChildTarget
    {
        public string Name { get; init; } = string.Empty;
    }

    private sealed class MissingParentSource
    {
        public MissingChildSource? Child { get; init; }
    }

    private sealed class MissingChildSource;

    private sealed class MissingParentTarget
    {
        public MissingChildTarget? Child { get; init; }
    }

    private sealed class MissingChildTarget;

    private sealed class CyclicSource
    {
        public bool TriggerCycle { get; init; }
    }

    private sealed class CyclicTarget;

    private class BaseSource;

    private sealed class DerivedSource : BaseSource;

    private sealed class PolymorphicTarget
    {
        public string Marker { get; init; } = string.Empty;
    }
}
