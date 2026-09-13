using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Tenancy;

namespace Skoleoverblikket.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenantContext)
	: DbContext(options)
{
	public DbSet<School> Schools => Set<School>();
	public DbSet<Staff> Staff => Set<Staff>();
	public DbSet<StaffInvitation> StaffInvitations => Set<StaffInvitation>();
	public DbSet<Class> Classes => Set<Class>();
	public DbSet<Course> Courses => Set<Course>();
	public DbSet<Room> Rooms => Set<Room>();
	public DbSet<TimeSlotTemplate> TimeSlotTemplates => Set<TimeSlotTemplate>();
	public DbSet<TimeSlotTemplateBreak> TimeSlotTemplateBreaks => Set<TimeSlotTemplateBreak>();
	public DbSet<TimeSlot> TimeSlots => Set<TimeSlot>();
	public DbSet<Schema> Schemas => Set<Schema>();
	public DbSet<SchemaSlot> SchemaSlots => Set<SchemaSlot>();
	public DbSet<SchoolFile> SchoolFiles => Set<SchoolFile>();
	public DbSet<SchoolFileFolder> SchoolFileFolders => Set<SchoolFileFolder>();
	public DbSet<CalendarEntry> CalendarEntries => Set<CalendarEntry>();
	public DbSet<Subscription> Subscriptions => Set<Subscription>();
	public DbSet<WeekPlan> WeekPlans => Set<WeekPlan>();
	public DbSet<WeekPlanSlot> WeekPlanSlots => Set<WeekPlanSlot>();
	public DbSet<WeekPlanSlotFile> WeekPlanSlotFiles => Set<WeekPlanSlotFile>();
	public DbSet<ClassPermission> ClassPermissions => Set<ClassPermission>();
	public DbSet<SfoShift> SfoShifts => Set<SfoShift>();
	public DbSet<SfoShiftStaff> SfoShiftStaff => Set<SfoShiftStaff>();
	public DbSet<SfoWeekPlan> SfoWeekPlans => Set<SfoWeekPlan>();
	public DbSet<SfoWeekPlanShift> SfoWeekPlanShifts => Set<SfoWeekPlanShift>();
	public DbSet<Parent> Parents => Set<Parent>();
	public DbSet<Student> Students => Set<Student>();
	public DbSet<ParentInvitation> ParentInvitations => Set<ParentInvitation>();
	public DbSet<AbsenceReport> AbsenceReports => Set<AbsenceReport>();
	public DbSet<SubscriptionModuleItem> SubscriptionModuleItems => Set<SubscriptionModuleItem>();
	public DbSet<Notification> Notifications => Set<Notification>();
	public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();
	public DbSet<ContactThread> ContactThreads => Set<ContactThread>();
	public DbSet<ContactMessage> ContactMessages => Set<ContactMessage>();
	public DbSet<Message> Messages => Set<Message>();
	public DbSet<VacationRegistrationWindow> VacationRegistrationWindows => Set<VacationRegistrationWindow>();
	public DbSet<VacationRegistrationEntry> VacationRegistrationEntries => Set<VacationRegistrationEntry>();
	public DbSet<GroupMessage> GroupMessages => Set<GroupMessage>();
	public DbSet<BoardMember> BoardMembers => Set<BoardMember>();
	public DbSet<BoardMemberInvitation> BoardMemberInvitations => Set<BoardMemberInvitation>();
	public DbSet<BoardFile> BoardFiles => Set<BoardFile>();
	public DbSet<BoardFileFolder> BoardFileFolders => Set<BoardFileFolder>();
	public DbSet<StaaMaalMedSnapshot> StaaMaalMedSnapshots => Set<StaaMaalMedSnapshot>();
	public DbSet<ClassChatMessage> ClassChatMessages => Set<ClassChatMessage>();
	public DbSet<ClassChatAttachment> ClassChatAttachments => Set<ClassChatAttachment>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		base.OnModelCreating(modelBuilder);

		modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

		// All tenant-scoped entities must implement ITenantScoped.
		// The global query filter below ensures every query is automatically
		// filtered to the current tenant — never bypass this.
		foreach (var entityType in modelBuilder.Model.GetEntityTypes())
		{
			var clrType = entityType.ClrType;
			var isTenantScoped = typeof(ITenantScoped).IsAssignableFrom(clrType);
			var isArchivable = typeof(IArchivable).IsAssignableFrom(clrType);

			if (!isTenantScoped && !isArchivable)
			{
				continue;
			}

			string methodName = (isTenantScoped, isArchivable) switch
			{
				(true, true) => nameof(SetTenantAndArchivableFilter),
				(true, false) => nameof(SetTenantFilter),
				(false, true) => nameof(SetArchivableFilter),
				_ => throw new UnreachableException(),
			};

			typeof(AppDbContext)
				.GetMethod(methodName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static)!
				.MakeGenericMethod(clrType)
				.Invoke(this, [modelBuilder]);
		}
	}

	private void SetTenantFilter<TEntity>(ModelBuilder modelBuilder)
		where TEntity : class, ITenantScoped
	{
		modelBuilder.Entity<TEntity>()
			.HasQueryFilter(e => e.TenantId == tenantContext.TenantId);
	}

	private static void SetArchivableFilter<TEntity>(ModelBuilder modelBuilder)
		where TEntity : class, IArchivable
	{
		modelBuilder.Entity<TEntity>()
			.HasQueryFilter(e => e.ArchivedAt == null);
	}

	private void SetTenantAndArchivableFilter<TEntity>(ModelBuilder modelBuilder)
		where TEntity : class, ITenantScoped, IArchivable
	{
		modelBuilder.Entity<TEntity>()
			.HasQueryFilter(e => e.TenantId == tenantContext.TenantId && e.ArchivedAt == null);
	}
}
