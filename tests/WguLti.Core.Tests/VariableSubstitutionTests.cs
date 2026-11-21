using FluentAssertions;
using WguLti.Core.Substitution;
using WguLti.Domain;

namespace WguLti.Core.Tests;

public class VariableSubstitutionTests
{
    [Fact]
    public void Substitutes_permitted_variables_and_leaves_the_rest_literal()
    {
        var values = new SubstitutionValues(
            "user-1", "Ada Lovelace", "ada@wgu.example", "ctx-1", "Data Structures II",
            "sec-1", "rl-1", "2026-12-01T00:00:00Z", "BSIT", "2026-FA",
            PrivacyLevels.NameOnly, new HashSet<string> { "Wgu.term" });

        VariableSubstitution.Apply("$User.id", values).Should().Be("user-1");
        VariableSubstitution.Apply("${Person.name.full}", values).Should().Be("Ada Lovelace");
        VariableSubstitution.Apply("$Person.email.primary", values).Should().Be("$Person.email.primary");
        VariableSubstitution.Apply("$Context.title", values).Should().Be("Data Structures II");
        VariableSubstitution.Apply("$CourseSection.sourcedId", values).Should().Be("$CourseSection.sourcedId");
        VariableSubstitution.Apply("$Wgu.term", values).Should().Be("2026-FA");
        VariableSubstitution.Apply("$Wgu.program.code", values).Should().Be("$Wgu.program.code");
        VariableSubstitution.Apply("$Not.a.variable", values).Should().Be("$Not.a.variable");
    }
}
