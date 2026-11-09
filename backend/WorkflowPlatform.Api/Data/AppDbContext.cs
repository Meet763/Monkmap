using Microsoft.EntityFrameworkCore;
using WorkflowPlatform.Api.Entities;

namespace WorkflowPlatform.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Workflow> Workflows => Set<Workflow>();
    public DbSet<WorkflowVersion> WorkflowVersions => Set<WorkflowVersion>();
    public DbSet<Execution> Executions => Set<Execution>();
    public DbSet<NodeExecution> NodeExecutions => Set<NodeExecution>();
    public DbSet<Connection> Connections => Set<Connection>();
    public DbSet<AccountMembership> AccountMemberships => Set<AccountMembership>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<WorkflowLock> WorkflowLocks => Set<WorkflowLock>();
    public DbSet<PermissionDefinition> Permissions => Set<PermissionDefinition>();
    public DbSet<PermissionPreset> PermissionPresets => Set<PermissionPreset>();
    public DbSet<PermissionPresetItem> PermissionPresetItems => Set<PermissionPresetItem>();
    public DbSet<MembershipPermission> MembershipPermissions => Set<MembershipPermission>();
    public DbSet<InvitationPermission> InvitationPermissions => Set<InvitationPermission>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>().HasIndex(u => u.Email).IsUnique();

        b.Entity<Workflow>()
            .HasOne(w => w.User)
            .WithMany(u => u.Workflows)
            .HasForeignKey(w => w.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<WorkflowVersion>()
            .HasOne(v => v.Workflow)
            .WithMany(w => w.Versions)
            .HasForeignKey(v => v.WorkflowId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<Execution>()
            .HasOne(e => e.Workflow)
            .WithMany(w => w.Executions)
            .HasForeignKey(e => e.WorkflowId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<Execution>().HasIndex(e => new { e.WorkflowId, e.StartedAt });

        b.Entity<NodeExecution>()
            .HasOne(n => n.Execution)
            .WithMany(e => e.NodeExecutions)
            .HasForeignKey(n => n.ExecutionId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<NodeExecution>().HasIndex(n => n.ExecutionId);

        // Store enums as strings for readability.
        b.Entity<Execution>().Property(e => e.Status).HasConversion<string>();
        b.Entity<Execution>().Property(e => e.TriggerType).HasConversion<string>();
        b.Entity<NodeExecution>().Property(n => n.Status).HasConversion<string>();

        b.Entity<Connection>()
            .HasOne(c => c.User)
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<Connection>().HasIndex(c => new { c.UserId, c.Provider });

        b.Entity<AccountMembership>().HasIndex(m => new { m.AccountOwnerId, m.UserId }).IsUnique();
        b.Entity<AccountMembership>().HasIndex(m => m.UserId);

        // ---- Permission catalog and grants ----
        b.Entity<PermissionDefinition>().ToTable("Permissions").HasKey(p => p.Key);
        b.Entity<PermissionDefinition>().Property(p => p.Key).HasMaxLength(64);
        b.Entity<PermissionDefinition>().Property(p => p.Label).HasMaxLength(64);
        b.Entity<PermissionDefinition>().Property(p => p.Description).HasMaxLength(255);
        b.Entity<PermissionDefinition>().HasData(PermissionSeed.Definitions);

        b.Entity<PermissionPreset>().ToTable("PermissionPresets").HasKey(p => p.Id);
        b.Entity<PermissionPreset>().Property(p => p.Name).HasMaxLength(64);
        b.Entity<PermissionPreset>().HasIndex(p => p.Name).IsUnique();
        b.Entity<PermissionPreset>().Property(p => p.Description).HasMaxLength(255);
        b.Entity<PermissionPreset>().HasData(PermissionSeed.Presets);

        b.Entity<PermissionPresetItem>().ToTable("PermissionPresetItems").HasKey(i => new { i.PresetId, i.PermissionKey });
        b.Entity<PermissionPresetItem>().Property(i => i.PermissionKey).HasMaxLength(64);
        b.Entity<PermissionPresetItem>()
            .HasOne(i => i.Preset).WithMany(p => p.Items)
            .HasForeignKey(i => i.PresetId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<PermissionPresetItem>()
            .HasOne<PermissionDefinition>().WithMany()
            .HasForeignKey(i => i.PermissionKey).OnDelete(DeleteBehavior.Cascade);
        b.Entity<PermissionPresetItem>().HasData(PermissionSeed.PresetItems);

        b.Entity<MembershipPermission>().ToTable("MembershipPermissions").HasKey(m => new { m.MembershipId, m.PermissionKey });
        b.Entity<MembershipPermission>().Property(m => m.PermissionKey).HasMaxLength(64);
        b.Entity<MembershipPermission>()
            .HasOne(m => m.Membership).WithMany(a => a.Grants)
            .HasForeignKey(m => m.MembershipId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<MembershipPermission>()
            .HasOne<PermissionDefinition>().WithMany()
            .HasForeignKey(m => m.PermissionKey).OnDelete(DeleteBehavior.Restrict);

        b.Entity<InvitationPermission>().ToTable("InvitationPermissions").HasKey(i => new { i.InvitationId, i.PermissionKey });
        b.Entity<InvitationPermission>().Property(i => i.PermissionKey).HasMaxLength(64);
        b.Entity<InvitationPermission>()
            .HasOne(i => i.Invitation).WithMany(inv => inv.Grants)
            .HasForeignKey(i => i.InvitationId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<InvitationPermission>()
            .HasOne<PermissionDefinition>().WithMany()
            .HasForeignKey(i => i.PermissionKey).OnDelete(DeleteBehavior.Restrict);

        b.Entity<Invitation>().HasIndex(i => i.TokenHash).IsUnique();
        b.Entity<Invitation>().HasIndex(i => new { i.AccountOwnerId, i.Email });

        b.Entity<WorkflowLock>()
            .HasKey(l => l.WorkflowId);
        b.Entity<WorkflowLock>()
            .HasOne(l => l.Workflow)
            .WithOne()
            .HasForeignKey<WorkflowLock>(l => l.WorkflowId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
