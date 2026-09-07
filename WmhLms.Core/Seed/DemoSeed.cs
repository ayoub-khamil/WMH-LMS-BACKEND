using Microsoft.EntityFrameworkCore;
using WmhLms.Core.Services;
using WmhLms.Data;
using WmhLms.Data.Entities;

namespace WmhLms.Core.Seed;

/// <summary>
/// Demo-only seed: one agent + the catalogue. The root manager is created by
/// RootManagerBootstrap in every environment, so it is not seeded here.
/// No explicit ids: Postgres identity columns do not advance past inserted
/// values, so the next insert would collide. No mock progress.
/// </summary>
public static class DemoSeed
{
    // Deliberately fake: a dev account must never be a real employee address.
    private const string AgentEmail = "agent@wmh.local";
    private const string AgentPassword = "dev-agent-password";

    public static async Task RunAsync(AppDbContext db, IPasswordService passwords)
    {
        await SeedAgentAsync(db, passwords);
        await SeedCoursesAsync(db);
    }

    /// <summary>Demo agent. Skipped once it exists.</summary>
    private static async Task SeedAgentAsync(AppDbContext db, IPasswordService passwords)
    {
        var email = Guard.Email(AgentEmail);
        if (await db.Users.AnyAsync(u => u.Email == email)) return;

        var agent = new User
        {
            FirstName = "Demo", LastName = "Agent", Email = email,
            Role = "agent", Status = "active",
            CreatedAt = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc)
        };
        agent.PasswordHash = passwords.Hash(AgentPassword);
        db.Users.Add(agent);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Demo catalogue. Seeded independently of the users so that
    /// POST /api/dev/reset (which keeps the root account) still restores the
    /// content on the next boot, instead of leaving an empty catalogue behind.
    /// </summary>
    private static async Task SeedCoursesAsync(AppDbContext db)
    {
        if (await db.Courses.AnyAsync()) return;

        var courses = new List<Course>
        {
            new()
            {
                Title = "BPO Tier 1 Customer Operations & Escalation Mastery",
                Description = "Core operational onboarding covering ticketing workflows, customer de-escalation protocols, and compliance requirements.",
                Status = "published",
                CreatedAt = new DateTime(2026, 1, 20, 10, 0, 0, DateTimeKind.Utc),
                Sections = new List<Section>
                {
                    new()
                    {
                        Title = "Section 1: Foundations & Call Etiquette", Order = 1,
                        Items = new List<Item>
                        {
                            new() { Title = "Executive Welcome & Culture Overview",
                                Type = "video", ContentUrl = "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
                                TextContent = "", Order = 1 },
                            new() { Title = "Standard Operating Procedures: Active Listening",
                                Type = "text", ContentUrl = "",
                                TextContent = "Active Listening in High-Paced BPO Environments\n\nActive listening is the cornerstone of frontline customer success. Follow the L.A.S.T. framework:\n\n- Listen: Allow the customer to speak uninterrupted for the first 45 seconds.\n- Acknowledge: Validate frustration with empathetic, neutral statements.\n- Solve: Diagnose the root cause using the knowledge base.\n- Thank: Thank them for their patience and confirm resolution.\n\nNever promise timelines or compensation beyond tier 1 authority.",
                                Order = 2 }
                        }
                    },
                    new()
                    {
                        Title = "Section 2: Escalation Protocols & Compliance", Order = 2,
                        Items = new List<Item>
                        {
                            new() { Title = "Managing Difficult Scenarios & De-escalation",
                                Type = "video", ContentUrl = "https://www.youtube.com/watch?v=kJQP7kiw5Fk",
                                TextContent = "", Order = 1 },
                            new() { Title = "Tier 1 Knowledge & Escalation Assessment",
                                Type = "quiz", ContentUrl = "", TextContent = "", Order = 2,
                                Questions = new List<Question>
                                {
                                    new() { Type = "multiple_choice",
                                        Prompt = "What is the very first step in the L.A.S.T. framework during an inbound customer dispute?",
                                        Options = new List<Option>
                                        {
                                            new() { Text = "Immediately offer a partial refund", IsCorrect = false },
                                            new() { Text = "Listen uninterrupted for the initial phase", IsCorrect = true },
                                            new() { Text = "Transfer the caller to a team lead", IsCorrect = false },
                                            new() { Text = "Place the caller on hold to read notes", IsCorrect = false }
                                        } },
                                    new() { Type = "multiple_answer",
                                        Prompt = "Which of the following scenarios are valid immediate escalation triggers to a Tier 2 Manager? (Select all correct)",
                                        Options = new List<Option>
                                        {
                                            new() { Text = "Customer requesting formal legal action or regulatory filing", IsCorrect = true },
                                            new() { Text = "Customer asking for general order tracking updates", IsCorrect = false },
                                            new() { Text = "Suspected account takeover / security credential compromise", IsCorrect = true },
                                            new() { Text = "Customer asking how to reset their forgotten password", IsCorrect = false }
                                        } },
                                    new() { Type = "true_false",
                                        Prompt = "Frontline Tier 1 agents are authorized to issue discretionary refunds greater than $500 without prior manager sign-off.",
                                        Options = new List<Option>
                                        {
                                            new() { Text = "True", IsCorrect = false },
                                            new() { Text = "False", IsCorrect = true }
                                        } }
                                } }
                        }
                    }
                }
            },
            new()
            {
                Title = "PCI-DSS Data Security & Privacy Compliance 2026",
                Description = "Mandatory security compliance training for all contact center personnel handling cardholder and personal data.",
                Status = "published",
                CreatedAt = new DateTime(2026, 2, 1, 11, 0, 0, DateTimeKind.Utc),
                Sections = new List<Section>
                {
                    new()
                    {
                        Title = "Section 1: Data Masking & Secure Data Entry", Order = 1,
                        Items = new List<Item>
                        {
                            new() { Title = "Handling Cardholder Data in the CRM",
                                Type = "text", ContentUrl = "",
                                TextContent = "Cardholder Data Security Standards (PCI-DSS)\n\n1. Never record CVV/CVC codes.\n2. Pause screen recording before reading card data into the gateway.\n3. Clean desk policy: no phones, pads, or USB devices on the floor.",
                                Order = 1 },
                            new() { Title = "PCI-DSS Compliance Check",
                                Type = "quiz", ContentUrl = "", TextContent = "", Order = 2,
                                Questions = new List<Question>
                                {
                                    new() { Type = "multiple_choice",
                                        Prompt = "Which of the following elements must NEVER be written down or stored under any circumstances?",
                                        Options = new List<Option>
                                        {
                                            new() { Text = "Customer first name", IsCorrect = false },
                                            new() { Text = "CVV / CVC card security code", IsCorrect = true },
                                            new() { Text = "Ticket ID number", IsCorrect = false }
                                        } }
                                } }
                        }
                    }
                }
            },
            new()
            {
                Title = "Omnichannel Live Chat & Tone Calibration",
                Description = "Guidelines for multi-chat concurrency, macro usage, and maintaining empathetic tone across asynchronous channels.",
                Status = "draft",
                CreatedAt = new DateTime(2026, 2, 15, 15, 30, 0, DateTimeKind.Utc),
                Sections = new List<Section>
                {
                    new()
                    {
                        Title = "Section 1: Concurrency Best Practices", Order = 1,
                        Items = new List<Item>
                        {
                            new() { Title = "Balancing 3+ Concurrent Chats",
                                Type = "text", ContentUrl = "",
                                TextContent = "Draft module for managing multiple chat queues efficiently.",
                                Order = 1 }
                        }
                    }
                }
            }
        };
        db.Courses.AddRange(courses);
        await db.SaveChangesAsync();
    }
}
