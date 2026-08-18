using FluentAssertions;
using PhxDbExplorer.Query;

namespace PhxDbExplorer.Tests;

public class ReadOnlySqlValidatorTests
{
    // ── accepted statements ──────────────────────────────────────────────────

    [Theory]
    [InlineData("SELECT * FROM Employees")]
    [InlineData("select TOP 10 FirstName, LastName from dbo.Employees where IsActive = 1")]
    [InlineData("SELECT COUNT(*) FROM Employees e JOIN Departments d ON e.DepartmentId = d.DepartmentId")]
    [InlineData("WITH recent AS (SELECT * FROM Employees) SELECT * FROM recent")]
    [InlineData("SELECT * FROM Employees WHERE Name = 'Robert''); DROP TABLE Students;--'")]
    [InlineData("SELECT [Update], [Delete] FROM \"Insert\"")]
    [InlineData("SELECT * FROM Employees -- trailing comment")]
    [InlineData("SELECT * FROM Employees;")]
    [InlineData("SELECT * FROM Employees;   ")]
    public void ValidateStatement_AllowsReadOnlyQueries(string sql)
    {
        var result = ReadOnlySqlValidator.ValidateStatement(sql);

        result.IsValid.Should().BeTrue(because: result.Error);
    }

    // ── rejected statements ──────────────────────────────────────────────────

    [Theory]
    [InlineData("DELETE FROM Employees")]
    [InlineData("UPDATE Employees SET Salary = 0")]
    [InlineData("INSERT INTO Employees (Name) VALUES ('x')")]
    [InlineData("DROP TABLE Employees")]
    [InlineData("TRUNCATE TABLE Employees")]
    [InlineData("EXEC sp_who")]
    [InlineData("CALL usp_deactivate_employee(1)")]
    [InlineData("SELECT * INTO Backup FROM Employees")]
    [InlineData("SELECT * FROM Employees; DROP TABLE Employees")]
    [InlineData("SELECT * FROM Employees; DELETE FROM Departments;")]
    [InlineData("WITH x AS (DELETE FROM Employees RETURNING *) SELECT * FROM x")]
    [InlineData("SELECT pg_sleep(60)")]
    [InlineData("SELECT * FROM OPENROWSET('SQLNCLI', 'x', 'SELECT 1')")]
    [InlineData("SET TRANSACTION READ WRITE")]
    [InlineData("BEGIN TRANSACTION")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-- just a comment")]
    public void ValidateStatement_RejectsAnythingNotReadOnly(string sql)
    {
        var result = ReadOnlySqlValidator.ValidateStatement(sql);

        result.IsValid.Should().BeFalse();
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ValidateStatement_CommentSmugglingIsStillDetected()
    {
        // Stripping the comment leaves "DROP" contiguous rather than hiding it
        var result = ReadOnlySqlValidator.ValidateStatement("SELECT 1; DR/**/OP TABLE Employees");

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateStatement_KeywordInsideStringLiteralIsAllowed()
    {
        var result = ReadOnlySqlValidator.ValidateStatement(
            "SELECT * FROM AuditLog WHERE Action = 'DELETE'");

        result.IsValid.Should().BeTrue(because: result.Error);
    }

    // ── fragments ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("IsActive = 1")]
    [InlineData("Salary > 50000 AND DepartmentId IN (1, 2, 3)")]
    [InlineData("Name LIKE '%o''brien%'")]
    [InlineData("DepartmentId IN (SELECT DepartmentId FROM Departments WHERE Active = 1)")]
    public void ValidateFragment_AllowsReadOnlyPredicates(string? fragment)
    {
        var result = ReadOnlySqlValidator.ValidateFragment(fragment, "where clause");

        result.IsValid.Should().BeTrue(because: result.Error);
    }

    [Theory]
    [InlineData("1 = 1; DROP TABLE Employees")]
    [InlineData("1 = 1) ; DELETE FROM Employees --")]
    [InlineData("1 = 1 AND (SELECT 1")]
    [InlineData("1 = 1 UNION SELECT * FROM Secrets) --")]
    public void ValidateFragment_RejectsStatementEscapes(string fragment)
    {
        var result = ReadOnlySqlValidator.ValidateFragment(fragment, "where clause");

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateFragment_ErrorNamesTheOffendingFragment()
    {
        var result = ReadOnlySqlValidator.ValidateFragment("HireDate DESC; DROP TABLE x", "order by clause");

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("order by clause");
    }
}
