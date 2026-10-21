namespace Skoleoverblikket.Api.Auth;

public static class Roles
{
	public const string Admin = "admin";
	public const string Staff = "staff";
	public const string Parent = "parent";
	public const string SuperAdmin = "superadmin";
	public const string Board = "board";
}

public static class Policies
{
	public const string EditClass = "EditClass";
	public const string EditWeekPlan = "EditWeekPlan";
	public const string ParentClassAccess = "ParentClassAccess";
	public const string SendGroupMessage = "SendGroupMessage";
	public const string CanAccessTeacherData = "CanAccessTeacherData";
}
