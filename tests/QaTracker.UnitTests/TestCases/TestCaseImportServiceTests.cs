using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using QaTracker.UnitTests.Attachments;
using QaTracker.Web.Attachments;
using QaTracker.Web.Data;
using QaTracker.Web.Defects;
using QaTracker.Web.Notifications;
using QaTracker.Web.Projects;
using QaTracker.Web.TestCases;

namespace QaTracker.UnitTests.TestCases;

public sealed class TestCaseImportServiceTests : IDisposable
{
    private const string Header = "Test Case Scope,Test Case Type,Test Case Scenario,Steps,DefectLink";

    private readonly SqliteConnection connection;
    private readonly IDbContextFactory<ApplicationDbContext> factory;
    private readonly FakeTimeProvider time = new(
        DateTimeOffset.Parse("2026-09-03T10:00:00Z", CultureInfo.InvariantCulture));
    private readonly ProjectService projects;
    private readonly TestScopeService scopes;
    private readonly TestCaseService cases;
    private readonly DefectService defects;
    private readonly Guid projectId;

    public TestCaseImportServiceTests()
    {
        connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        factory = new TestDbContextFactory(options);

        using (var db = factory.CreateDbContext())
        {
            db.Database.EnsureCreated();
            db.Users.Add(new ApplicationUser { Id = "user-1", UserName = "qa", Email = "qa@test.local", FullName = "QA One" });
            db.SaveChanges();
        }

        var attachments = new AttachmentService(factory, new FakeFileStorage(), time, NullLogger<AttachmentService>.Instance);
        projects = new ProjectService(factory, time, attachments, new NotificationService(factory, time));
        var notifications = new NotificationService(factory, time);
        scopes = new TestScopeService(factory, time, projects, attachments);
        cases = new TestCaseService(factory, time, attachments, new NotificationService(factory, time));
        defects = new DefectService(factory, time, projects, attachments, notifications);
        projectId = projects.CreateAsync("Proj", null, null, [], "user-1").GetAwaiter().GetResult().Id;
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }

    private TestCaseImportService Sut() => new(scopes, cases, defects, projects);

    private static ParsedImport Parse(params string[] lines) =>
        TestCaseImportParser.Parse(Header + "\n" + string.Join("\n", lines) + "\n");

    private Task<Defect> RaiseDefectAsync(string summary = "A bug") =>
        defects.CreateAsync(projectId, new DefectInput(summary, null, null, null, null), "user-1");

    // --- BuildPlanAsync -------------------------------------------------------

    [Fact]
    public async Task Plan_reports_a_new_scope_as_an_info()
    {
        var plan = await Sut().BuildPlanAsync(projectId, Parse("Auth,Functional,Login works,do,"));

        Assert.True(plan.CanImport);
        Assert.Contains(plan.Infos, i => i.Message.Contains("will be created"));
        Assert.Equal(1, plan.ScopesToCreate);
        Assert.Equal(1, plan.CasesToCreate);
    }

    [Fact]
    public async Task Plan_reports_an_existing_scope_of_the_same_type_as_an_info()
    {
        await scopes.CreateAsync(projectId, TestCaseKind.Functional, "Auth", "user-1");

        var plan = await Sut().BuildPlanAsync(projectId, Parse("Auth,Functional,Login works,do,"));

        Assert.True(plan.CanImport);
        Assert.Contains(plan.Infos, i => i.Message.Contains("will be added") || i.Message.Contains("will change"));
    }

    [Fact]
    public async Task Plan_blocks_when_an_existing_scope_has_a_different_type()
    {
        await scopes.CreateAsync(projectId, TestCaseKind.Functional, "Auth", "user-1");

        var plan = await Sut().BuildPlanAsync(projectId, Parse("Auth,Non-Functional,Login is fast,do,"));

        Assert.False(plan.CanImport);
        Assert.Contains(plan.Errors, e => e.Message.Contains("is Functional, but the file says Non-functional"));
    }

    [Fact]
    public async Task Plan_warns_that_an_existing_case_will_be_overwritten()
    {
        var scope = await scopes.CreateAsync(projectId, TestCaseKind.Functional, "Auth", "user-1");
        await cases.CreateAsync(scope.Id, new TestCaseInput("Login works", "old step"), "user-1");

        var plan = await Sut().BuildPlanAsync(projectId, Parse("Auth,Functional,Login works,new step,"));

        var casePlan = plan.Scopes.Single().Cases.Single();
        Assert.Equal(ImportAction.Update, casePlan.Action);
        Assert.True(casePlan.StepsChanged);
        Assert.Equal(
            [new DiffLine(LineChange.Removed, "old step"), new DiffLine(LineChange.Added, "new step")],
            casePlan.StepsDiff);
        Assert.Contains(plan.Warnings, w => w.Message.Contains("steps will be replaced"));
    }

    [Fact]
    public async Task Plan_leaves_a_matching_case_unchanged_ignoring_whitespace_and_line_endings()
    {
        var scope = await scopes.CreateAsync(projectId, TestCaseKind.Functional, "Auth", "user-1");
        await cases.CreateAsync(scope.Id, new TestCaseInput("Login works", "open\r\n\r\n  submit  "), "user-1");

        var plan = await Sut().BuildPlanAsync(projectId, Parse("Auth,Functional,Login works,open,", ",,,submit,"));

        var casePlan = plan.Scopes.Single().Cases.Single();
        Assert.Equal(ImportAction.Unchanged, casePlan.Action);
        Assert.False(casePlan.HasChanges);
        Assert.False(plan.HasChanges);
        Assert.Equal(1, plan.CasesUnchanged);
        Assert.Empty(plan.Warnings);
        Assert.Contains(plan.Infos, i => i.Message.Contains("already matches the file"));
    }

    [Fact]
    public async Task Plan_flags_a_scenario_wording_change_as_an_update()
    {
        var scope = await scopes.CreateAsync(projectId, TestCaseKind.Functional, "Auth", "user-1");
        await cases.CreateAsync(scope.Id, new TestCaseInput("login works", "open"), "user-1");

        var plan = await Sut().BuildPlanAsync(projectId, Parse("Auth,Functional,Login works,open,"));

        var casePlan = plan.Scopes.Single().Cases.Single();
        Assert.Equal(ImportAction.Update, casePlan.Action);
        Assert.True(casePlan.ScenarioChanged);
        Assert.False(casePlan.StepsChanged);
        Assert.Equal("login works", casePlan.ExistingScenario);
    }

    [Fact]
    public async Task Plan_counts_a_new_link_on_an_unchanged_case_as_a_change()
    {
        await RaiseDefectAsync();
        var scope = await scopes.CreateAsync(projectId, TestCaseKind.Functional, "Auth", "user-1");
        await cases.CreateAsync(scope.Id, new TestCaseInput("Login works", "open"), "user-1");

        var plan = await Sut().BuildPlanAsync(projectId, Parse("Auth,Functional,Login works,open,D-1"));

        var casePlan = plan.Scopes.Single().Cases.Single();
        Assert.Equal(ImportAction.Unchanged, casePlan.Action);
        Assert.True(casePlan.HasChanges);
        Assert.Equal(1, plan.LinksToAdd);
        Assert.Contains(plan.Infos, i => i.Message.Contains(": 1 defect link will be added."));
    }

    [Fact]
    public async Task Apply_leaves_unchanged_cases_untouched()
    {
        var scope = await scopes.CreateAsync(projectId, TestCaseKind.Functional, "Auth", "user-1");
        var tc = await cases.CreateAsync(scope.Id, new TestCaseInput("Login works", "open"), "user-1");
        var before = (await cases.GetAsync(tc.Id))!.UpdatedUtc;
        time.Advance(TimeSpan.FromMinutes(5));

        var result = await Sut().ApplyAsync(projectId, Parse("Auth,Functional,Login works,open,"), "user-1");

        Assert.Equal(0, result.CasesUpdated);
        Assert.Equal(before, (await cases.GetAsync(tc.Id))!.UpdatedUtc);
        Assert.DoesNotContain(await cases.ListHistoryAsync(tc.Id), e => e.Kind == TestCaseEventKind.Edited);
    }

    [Fact]
    public async Task Plan_shows_a_clean_defect_link_as_an_example()
    {
        await RaiseDefectAsync();

        var plan = await Sut().BuildPlanAsync(projectId, Parse("Auth,Functional,Login works,do,D-1"));

        Assert.Contains(plan.Infos, i => i.Message.Contains("D-1 will be linked"));
        Assert.Single(plan.Scopes.Single().Cases.Single().DefectLinks);
    }

    [Fact]
    public async Task Plan_notes_a_defect_link_that_already_exists()
    {
        var defect = await RaiseDefectAsync();
        var scope = await scopes.CreateAsync(projectId, TestCaseKind.Functional, "Auth", "user-1");
        var tc = await cases.CreateAsync(scope.Id, new TestCaseInput("Login works", null), "user-1");
        await defects.LinkTestCaseAsync(defect.Id, tc.Id);

        var plan = await Sut().BuildPlanAsync(projectId, Parse("Auth,Functional,Login works,do,D-1"));

        // Not a change, so no note about it — and no new link counted.
        Assert.DoesNotContain(plan.Issues, i => i.Message.Contains("D-1"));
        var casePlan = plan.Scopes.Single().Cases.Single();
        Assert.True(casePlan.DefectLinks.Single().AlreadyLinked);
        Assert.Empty(casePlan.NewLinks);
    }

    [Fact]
    public async Task Plan_warns_about_an_unknown_defect_number()
    {
        var plan = await Sut().BuildPlanAsync(projectId, Parse("Auth,Functional,Login works,do,D-99"));

        Assert.True(plan.CanImport);
        Assert.Contains(plan.Warnings, w => w.Message.Contains("D-99") && w.Message.Contains("doesn't exist"));
    }

    [Fact]
    public async Task Plan_warns_about_an_unparseable_defect_token()
    {
        var plan = await Sut().BuildPlanAsync(projectId, Parse("Auth,Functional,Login works,do,see-jira"));

        Assert.Contains(plan.Warnings, w => w.Message.Contains("isn't a defect reference"));
    }

    [Fact]
    public async Task Plan_warns_when_a_defect_is_currently_linked_elsewhere()
    {
        var defect = await RaiseDefectAsync();
        var scope = await scopes.CreateAsync(projectId, TestCaseKind.Functional, "Auth", "user-1");
        var other = await cases.CreateAsync(scope.Id, new TestCaseInput("Some other case", null), "user-1");
        await defects.LinkTestCaseAsync(defect.Id, other.Id);

        var plan = await Sut().BuildPlanAsync(projectId, Parse("Auth,Functional,Login works,do,D-1"));

        Assert.Contains(plan.Warnings, w => w.Message.Contains("already linked to \"Some other case\""));
    }

    // --- ApplyAsync ---------------------------------------------------------

    [Fact]
    public async Task Apply_creates_scopes_cases_steps_and_links_and_activates_the_project()
    {
        await RaiseDefectAsync();
        var csv = Parse(
            "Auth,Functional,Login works,open,D-1",
            ",,,submit,",
            "Perf,Non Functional,Search is fast,,");

        var result = await Sut().ApplyAsync(projectId, csv, "user-1");

        Assert.True(result.Committed);
        Assert.Equal(2, result.ScopesCreated);
        Assert.Equal(2, result.CasesCreated);
        Assert.Equal(1, result.DefectsLinked);

        var stored = await scopes.ListForProjectAsync(projectId);
        var login = stored.Single(s => s.Name == "Auth").Cases.Single();
        Assert.Equal("open\nsubmit", login.Steps);
        Assert.Equal(ProjectStatus.InFlight, (await projects.GetAsync(projectId))!.Status);
        Assert.Single(await defects.ListForTestCaseAsync(login.Id));
    }

    [Fact]
    public async Task Apply_overwrites_steps_on_an_existing_case_but_keeps_its_result()
    {
        var scope = await scopes.CreateAsync(projectId, TestCaseKind.Functional, "Auth", "user-1");
        var tc = await cases.CreateAsync(scope.Id, new TestCaseInput("Login works", "old"), "user-1");
        await cases.SetResultAsync(tc.Id, TestResult.Failed);

        var result = await Sut().ApplyAsync(projectId, Parse("Auth,Functional,Login works,brand new steps,"), "user-1");

        Assert.Equal(1, result.CasesUpdated);
        var reloaded = await cases.GetAsync(tc.Id);
        Assert.Equal("brand new steps", reloaded!.Steps);
        Assert.Equal(TestResult.Failed, reloaded.Result);
    }

    [Fact]
    public async Task Apply_is_idempotent()
    {
        await RaiseDefectAsync();
        var csv = Parse("Auth,Functional,Login works,open,D-1");
        var sut = Sut();

        await sut.ApplyAsync(projectId, csv, "user-1");
        var second = await sut.ApplyAsync(projectId, csv, "user-1");

        Assert.Equal(0, second.ScopesCreated);
        Assert.Equal(0, second.CasesCreated);
        Assert.Equal(0, second.CasesUpdated); // nothing differs the second time round
        Assert.Equal(0, second.DefectsLinked);

        Assert.Single((await scopes.ListForProjectAsync(projectId)).Single().Cases);
        var tc = (await scopes.ListForProjectAsync(projectId)).Single().Cases.Single();
        Assert.Single(await defects.ListForTestCaseAsync(tc.Id));
    }

    [Fact]
    public async Task Export_then_re_import_updates_the_same_project_in_place()
    {
        var d1 = await RaiseDefectAsync();
        await Sut().ApplyAsync(projectId, Parse(
            "Auth,Functional,Login works,open,D-1",
            ",,,submit,",
            "Perf,Non Functional,Search is fast,,"), "user-1");

        var stored = await scopes.ListForProjectAsync(projectId);
        var byCase = await TestCaseCsv.DefectNumbersByCaseAsync(defects, projectId);
        var login = stored.Single(s => s.Name == "Auth").Cases.Single();
        Assert.Equal([d1.Number], byCase[login.Id]);

        var csv = TestCaseCsv.Export(stored, byCase);
        var reparsed = TestCaseImportParser.Parse(csv);
        var result = await Sut().ApplyAsync(projectId, reparsed, "user-1");

        Assert.True(result.Committed);
        Assert.Equal(0, result.ScopesCreated);
        Assert.Equal(0, result.CasesCreated);
        Assert.Equal(0, result.CasesUpdated); // the export matches the project exactly
        Assert.Equal(2, (await scopes.ListForProjectAsync(projectId)).Count);
        Assert.Single(await defects.ListForTestCaseAsync(login.Id));
    }

    [Fact]
    public async Task Apply_refuses_when_the_plan_has_blocking_errors()
    {
        await scopes.CreateAsync(projectId, TestCaseKind.Functional, "Auth", "user-1");

        var result = await Sut().ApplyAsync(projectId, Parse("Auth,Non-Functional,Login is fast,do,"), "user-1");

        Assert.False(result.Committed);
        Assert.Empty((await scopes.ListForProjectAsync(projectId)).Single().Cases);
    }

    // --- IDs (S-n / T-n) ----------------------------------------------------------

    private const string IdHeader = "Scope ID,Test Case Scope,Test Case Type,Test Case ID,Test Case Scenario,Steps,DefectLink";

    private static ParsedImport ParseWithIds(params string[] lines) =>
        TestCaseImportParser.Parse(IdHeader + "\n" + string.Join("\n", lines) + "\n");

    private async Task<string> ExportAsync() =>
        TestCaseCsv.Export(await scopes.ListForProjectAsync(projectId), new Dictionary<Guid, IReadOnlyList<int>>());

    [Fact]
    public async Task Scopes_and_cases_get_ids_unique_across_the_project()
    {
        var auth = await scopes.CreateAsync(projectId, TestCaseKind.Functional, "Auth", "user-1");
        var perf = await scopes.CreateAsync(projectId, TestCaseKind.NonFunctional, "Perf", "user-1");
        var a = await cases.CreateAsync(auth.Id, new TestCaseInput("Login", null), "user-1");
        var b = await cases.CreateAsync(perf.Id, new TestCaseInput("Fast", null), "user-1");
        var c = await cases.CreateAsync(auth.Id, new TestCaseInput("Logout", null), "user-1");

        Assert.Equal((1, 2), (auth.Number, perf.Number));
        Assert.Equal([1, 2, 3], new[] { a.Number, b.Number, c.Number });
        Assert.Contains(",Auth,Functional,", await ExportAsync());
        Assert.StartsWith(IdHeader, await ExportAsync());
        Assert.Contains("S-1,Auth,Functional,T-1,Login,,", await ExportAsync());
    }

    [Fact]
    public async Task Ids_let_a_file_rename_a_scope_and_a_case_in_place()
    {
        var auth = await scopes.CreateAsync(projectId, TestCaseKind.Functional, "Auth", "user-1");
        var login = await cases.CreateAsync(auth.Id, new TestCaseInput("Login", "do"), "user-1");

        var parsed = ParseWithIds("S-1,Authentication,Functional,T-1,Log in with a password,do,");
        var plan = await Sut().BuildPlanAsync(projectId, parsed);

        Assert.True(plan.CanImport);
        var scope = Assert.Single(plan.Scopes);
        Assert.True(scope.Renamed);
        Assert.Equal("Auth", scope.ExistingName);
        var @case = Assert.Single(scope.Cases);
        Assert.Equal(ImportAction.Update, @case.Action);
        Assert.True(@case.ScenarioChanged);
        Assert.Equal(1, @case.Number);

        var result = await Sut().ApplyAsync(projectId, parsed, "user-1");
        Assert.Equal((0, 1, 1, 0), (result.ScopesCreated, result.ScopesRenamed, result.CasesUpdated, result.CasesCreated));
        var after = Assert.Single(await scopes.ListForProjectAsync(projectId));
        Assert.Equal(("Authentication", 1), (after.Name, after.Number));
        Assert.Equal(("Log in with a password", 1, login.Id), (after.Cases.Single().Scenario, after.Cases.Single().Number, after.Cases.Single().Id));
    }

    [Fact]
    public async Task A_case_given_another_scope_by_id_is_moved_with_a_warning()
    {
        var auth = await scopes.CreateAsync(projectId, TestCaseKind.Functional, "Auth", "user-1");
        await scopes.CreateAsync(projectId, TestCaseKind.Functional, "Account", "user-1");
        var login = await cases.CreateAsync(auth.Id, new TestCaseInput("Login", null), "user-1");
        await cases.SetResultAsync(login.Id, TestResult.Passed, "user-1");

        var parsed = ParseWithIds("S-2,Account,Functional,T-1,Login,,");
        var plan = await Sut().BuildPlanAsync(projectId, parsed);

        var moved = plan.Scopes.Single().Cases.Single();
        Assert.Equal("Auth", moved.MovedFrom);
        Assert.Equal(ImportAction.Unchanged, moved.Action);
        Assert.True(moved.HasChanges);
        Assert.Contains(plan.Warnings, w => w.Message.Contains("will move from scope \"Auth\" to \"Account\""));

        var result = await Sut().ApplyAsync(projectId, parsed, "user-1");
        Assert.Equal(1, result.CasesMoved);
        var after = await scopes.ListForProjectAsync(projectId);
        var account = after.Single(s => s.Name == "Account").Cases.Single();
        Assert.Equal((login.Id, TestResult.Passed), (account.Id, account.Result));
        Assert.Empty(after.Single(s => s.Name == "Auth").Cases);
    }

    [Fact]
    public async Task A_missing_type_warns_for_a_new_scope_but_follows_an_existing_one()
    {
        await scopes.CreateAsync(projectId, TestCaseKind.NonFunctional, "Perf", "user-1");

        var plan = await Sut().BuildPlanAsync(projectId, Parse("Perf,,Fast,,", "Auth,,Login,,"));

        Assert.True(plan.CanImport);
        Assert.Equal(TestCaseKind.NonFunctional, plan.Scopes[0].Kind);
        Assert.False(plan.Scopes[0].KindAssumed);
        Assert.Equal(TestCaseKind.Functional, plan.Scopes[1].Kind);
        Assert.True(plan.Scopes[1].KindAssumed);
        Assert.Single(plan.Warnings, w => w.Message.Contains("No test case type was given for the new scope \"Auth\", so it will be created as Functional"));
    }

    [Fact]
    public async Task A_name_that_exists_in_another_scope_is_flagged()
    {
        var auth = await scopes.CreateAsync(projectId, TestCaseKind.Functional, "Auth", "user-1");
        await cases.CreateAsync(auth.Id, new TestCaseInput("Login", null), "user-1");

        var plan = await Sut().BuildPlanAsync(projectId, Parse("Account,Functional,Login,,", "Account,Functional,Other,,", "Billing,Functional,Other,,"));

        Assert.True(plan.CanImport);
        Assert.Contains(plan.Warnings, w => w.Message.Contains("\"Login\" already exists in scope \"Auth\" (T-1)"));
        Assert.Contains(plan.Warnings, w => w.Message.Contains("\"Other\" in \"Account\" is also in \"Billing\" (row 4)"));
        Assert.Contains(plan.Warnings, w => w.Message.Contains("\"Other\" in \"Billing\" is also in \"Account\" (row 3)"));
    }

    [Fact]
    public async Task Duplicate_names_in_a_scope_unknown_ids_and_clashing_renames_are_reported()
    {
        var auth = await scopes.CreateAsync(projectId, TestCaseKind.Functional, "Auth", "user-1");
        await cases.CreateAsync(auth.Id, new TestCaseInput("Login", null), "user-1");
        await cases.CreateAsync(auth.Id, new TestCaseInput("Logout", null), "user-1");

        var duplicate = await Sut().BuildPlanAsync(projectId, ParseWithIds("S-1,Auth,Functional,T-1,Login,,", ",,,T-9,Login,,"));
        Assert.Contains(duplicate.Errors, e => e.Message.Contains("more than one test case called \"Login\""));

        var unknown = await Sut().BuildPlanAsync(projectId, ParseWithIds("S-7,New scope,Functional,T-99,Fresh,,"));
        Assert.True(unknown.CanImport);
        Assert.Contains(unknown.Warnings, w => w.Message.Contains("Scope ID S-7 isn't in this project"));
        Assert.Contains(unknown.Warnings, w => w.Message.Contains("Test case ID T-99 isn't in this project"));

        var clash = await Sut().BuildPlanAsync(projectId, ParseWithIds("S-1,Auth,Functional,T-1,Logout,,"));
        Assert.Contains(clash.Errors, e => e.Message.Contains("T-1 can't be renamed in \"Auth\" as \"Logout\" — T-2 there already has that name"));

        var badId = ParseWithIds("S-x,Auth,Functional,,Login,,");
        Assert.Contains(badId.Errors, e => e.Message.Contains("isn't a valid scope ID"));
    }

    [Fact]
    public async Task An_exported_file_re_imports_with_nothing_to_change()
    {
        var auth = await scopes.CreateAsync(projectId, TestCaseKind.Functional, "Auth", "user-1");
        await cases.CreateAsync(auth.Id, new TestCaseInput("Login", "a\nb"), "user-1");

        var plan = await Sut().BuildPlanAsync(projectId, TestCaseImportParser.Parse(await ExportAsync()));

        Assert.True(plan.CanImport);
        Assert.False(plan.HasChanges);
    }

    // --- wording: always what the import will do --------------------------------------

    private async Task<(TestScope Auth, TestScope Perf)> SeedAuthAndPerfAsync()
    {
        var auth = await scopes.CreateAsync(projectId, TestCaseKind.Functional, "Auth", "user-1");
        var perf = await scopes.CreateAsync(projectId, TestCaseKind.NonFunctional, "Perf", "user-1");
        await cases.CreateAsync(auth.Id, new TestCaseInput("Login", "open\nsubmit"), "user-1");   // T-1
        await cases.CreateAsync(auth.Id, new TestCaseInput("Logout", null), "user-1");             // T-2
        await cases.CreateAsync(perf.Id, new TestCaseInput("Fast search", null), "user-1");        // T-3
        return (auth, perf);
    }

    private static IEnumerable<string> Infos(ImportPlan plan) => plan.Infos.Select(i => i.Message);

    [Fact]
    public async Task Notes_say_what_will_happen_not_what_already_exists()
    {
        await SeedAuthAndPerfAsync();

        var plan = await Sut().BuildPlanAsync(projectId, ParseWithIds(
            "S-1,Authentication,Functional,T-1,Sign in,open,", ",,,,,submit,",
            ",Session,Functional,T-2,Logout,,",
            ",Billing,Functional,,Invoice,create,", ",,,,,send,"));

        Assert.True(plan.CanImport);
        Assert.Contains("S-1 \"Auth\" will be renamed to \"Authentication\".", Infos(plan));
        Assert.Contains("In S-1 \"Authentication\": 1 test case will be updated.", Infos(plan));
        Assert.Contains("T-1 \"Login\" will be renamed to \"Sign in\".", Infos(plan));
        Assert.Contains("New Functional scope \"Session\" will be created with 1 test case moved in from other scopes.", Infos(plan));
        Assert.Contains("New Functional scope \"Billing\" will be created with 1 new test case.", Infos(plan));
        Assert.Contains("New test case \"Invoice\" will be added to \"Billing\" with 2 steps.", Infos(plan));
        Assert.DoesNotContain(Infos(plan), m => m.Contains("already exists"));
        Assert.Contains(plan.Warnings, w => w.Message.StartsWith("T-2 \"Logout\" will move from scope \"Auth\" to \"Session\""));
    }

    [Fact]
    public async Task Two_ids_renamed_to_one_name_is_an_error()
    {
        await SeedAuthAndPerfAsync();

        var plan = await Sut().BuildPlanAsync(projectId, ParseWithIds(
            "S-1,new-scope,Functional,T-1,Login,open,", ",,,,,submit,", "S-2,new-scope,,T-3,Fast search,,"));

        Assert.False(plan.CanImport);
        Assert.Single(plan.Errors, e => e.Message.Contains("More than one scope in the file is called \"new-scope\" (S-1 (row 2), S-2 (row 4))"));
    }

    [Fact]
    public async Task Scopes_can_swap_names_by_id()
    {
        var (auth, perf) = await SeedAuthAndPerfAsync();
        var parsed = ParseWithIds("S-1,Perf,Functional,T-1,Login,open,", ",,,,,submit,", ",,,T-2,Logout,,",
            "S-2,Auth,Non-Functional,T-3,Fast search,,");

        var plan = await Sut().BuildPlanAsync(projectId, parsed);
        Assert.True(plan.CanImport);
        Assert.Contains("Nothing else in S-1 will change (2 test cases checked).", Infos(plan));

        await Sut().ApplyAsync(projectId, parsed, "user-1");
        var after = await scopes.ListForProjectAsync(projectId);
        Assert.Equal("Perf", after.Single(s => s.Id == auth.Id).Name);
        Assert.Equal("Auth", after.Single(s => s.Id == perf.Id).Name);
    }

    [Fact]
    public async Task A_blocked_rename_is_not_described_as_happening()
    {
        await SeedAuthAndPerfAsync();

        var scopeClash = await Sut().BuildPlanAsync(projectId, ParseWithIds("S-1,Perf,Functional,T-1,Login,open,", ",,,,,submit,"));
        Assert.Contains(scopeClash.Errors, e => e.Message.StartsWith("S-1 \"Auth\" can't be renamed to \"Perf\""));
        Assert.DoesNotContain(Infos(scopeClash), m => m.Contains("will be renamed"));

        var caseClash = await Sut().BuildPlanAsync(projectId, ParseWithIds("S-1,Auth,Functional,T-1,Logout,open,", ",,,,,submit,"));
        Assert.Contains(caseClash.Errors, e => e.Message.StartsWith("T-1 can't be renamed in \"Auth\" as \"Logout\""));
        Assert.DoesNotContain(Infos(caseClash), m => m.Contains("will be renamed"));
    }

    [Fact]
    public async Task Duplicates_are_reported_once_not_again_as_double_matches()
    {
        await SeedAuthAndPerfAsync();

        var sameId = await Sut().BuildPlanAsync(projectId, ParseWithIds("S-1,Auth,Functional,T-1,Login,open,", "S-1,Auth,Functional,T-1,Login again,,"));
        Assert.Single(sameId.Errors);
        Assert.Contains("T-1 is used for more than one test case (rows 2, 3)", sameId.Errors.Single().Message);

        var sameName = await Sut().BuildPlanAsync(projectId, Parse("Auth,Functional,Login,open,", "Auth,Functional,Logout,,", "Auth,Functional,Login,again,"));
        Assert.Single(sameName.Errors);
    }

    [Fact]
    public async Task Messages_carry_their_row_without_repeating_it_in_the_text()
    {
        var parsed = ParseWithIds("S-x,Auth,Functional,T-?,Login,open,");

        Assert.All(parsed.Errors, e =>
        {
            Assert.Equal(2, e.Row);
            Assert.DoesNotContain("Row 2", e.Message);
        });
    }

    public void Dispose() => connection.Dispose();
}
