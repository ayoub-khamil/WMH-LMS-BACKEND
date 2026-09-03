using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using WmhLms.Core.Services;
using WmhLms.Data;
using WmhLms.Data.Entities;

namespace WmhLms.Core.Seed;

/// <summary>Demo-only seed: root manager + agent + catalogue. No mock progress.</summary>
public static class DemoSeed
{
    public static async Task RunAsync(AppDbContext db, IPasswordService passwords, IConfiguration config)
    {
        if (await db.Users.AnyAsync()) return;

        var now = DateTime.UtcNow;
        var managerEmail = config["Seed:ManagerEmail"] ?? "ayoub.khamil@watermelon-hub.com";
        var managerPassword = config["Seed:ManagerPassword"] ?? "ayoub1234";
        var agentEmail = config["Seed:AgentEmail"] ?? "khalid.khamil@watermelon-hub.com";
        var agentPassword = config["Seed:AgentPassword"] ?? "khalid1234";

        var manager = new User
        {
            Id = 1, FirstName = "Ayoub", LastName = "Khamil", Email = managerEmail,
            Role = "manager", Status = "active", IsRoot = true,
            CreatedAt = new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc)
        };
        manager.PasswordHash = passwords.Hash(managerPassword);
        var agent = new User
        {
            Id = 2, FirstName = "Khalid", LastName = "Khamil", Email = agentEmail,
            Role = "agent", Status = "active",
            CreatedAt = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc)
        };
        agent.PasswordHash = passwords.Hash(agentPassword);
        db.Users.AddRange(manager, agent);

        var courses = new List<Course>
        {
            new()
            {
                Id = 101,
                Title = "BPO Tier 1 Customer Operations & Escalation Mastery",
                Description = "Core operational onboarding covering ticketing workflows, customer de-escalation protocols, and compliance requirements.",
                Status = "published",
                CreatedAt = new DateTime(2026, 1, 20, 10, 0, 0, DateTimeKind.Utc),
                Sections = new List<Section>
                {
                    new()
                    {
                        Id = 1, Title = "Section 1: Foundations & Call Etiquette", Order = 1,
                        Items = new List<Item>
                        {
                            new() { Id = 11, Title = "Executive Welcome & Culture Overview",
                                Type = "video", ContentUrl = "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
                                TextContent = "", Order = 1 },
                            new() { Id = 12, Title = "Standard Operating Procedures: Active Listening",
                                Type = "text", ContentUrl = "",
                                TextContent = "Active Listening in High-Paced BPO Environments\n\nActive listening is the cornerstone of frontline customer success. Follow the L.A.S.T. framework:\n\n- Listen: Allow the customer to speak uninterrupted for the first 45 seconds.\n- Acknowledge: Validate frustration with empathetic, neutral statements.\n- Solve: Diagnose the root cause using the knowledge base.\n- Thank: Thank them for their patience and confirm resolution.\n\nNever promise timelines or compensation beyond tier 1 authority.",
                                Order = 2 }
                        }
                    },
                    new()
                    {
                        Id = 2, Title = "Section 2: Escalation Protocols & Compliance", Order = 2,
                        Items = new List<Item>
                        {
                            new() { Id = 21, Title = "Managing Difficult Scenarios & De-escalation",
                                Type = "video", ContentUrl = "https://www.youtube.com/watch?v=kJQP7kiw5Fk",
                                TextContent = "", Order = 1 },
                            new() { Id = 22, Title = "Tier 1 Knowledge & Escalation Assessment",
                                Type = "quiz", ContentUrl = "", TextContent = "", Order = 2,
                                Questions = new List<Question>
                                {
                                    new() { Id = 201, Type = "multiple_choice",
                                        Prompt = "What is the very first step in the L.A.S.T. framework during an inbound customer dispute?",
                                        Options = new List<Option>
                                        {
                                            new() { Id = 1, Text = "Immediately offer a partial refund", IsCorrect = false },
                                            new() { Id = 2, Text = "Listen uninterrupted for the initial phase", IsCorrect = true },
                                            new() { Id = 3, Text = "Transfer the caller to a team lead", IsCorrect = false },
                                            new() { Id = 4, Text = "Place the caller on hold to read notes", IsCorrect = false }
                                        } },
                                    new() { Id = 202, Type = "multiple_answer",
                                        Prompt = "Which of the following scenarios are valid immediate escalation triggers to a Tier 2 Manager? (Select all correct)",
                                        Options = new List<Option>
                                        {
                                            new() { Id = 5, Text = "Customer requesting formal legal action or regulatory filing", IsCorrect = true },
                                            new() { Id = 6, Text = "Customer asking for general order tracking updates", IsCorrect = false },
                                            new() { Id = 7, Text = "Suspected account takeover / security credential compromise", IsCorrect = true },
                                            new() { Id = 8, Text = "Customer asking how to reset their forgotten password", IsCorrect = false }
                                        } },
                                    new() { Id = 203, Type = "true_false",
                                        Prompt = "Frontline Tier 1 agents are authorized to issue discretionary refunds greater than $500 without prior manager sign-off.",
                                        Options = new List<Option>
                                        {
                                            new() { Id = 9, Text = "True", IsCorrect = false },
                                            new() { Id = 10, Text = "False", IsCorrect = true }
                                        } }
                                } }
                        }
                    }
                }
            },
            new()
            {
                Id = 102,
                Title = "PCI-DSS Data Security & Privacy Compliance 2026",
                Description = "Mandatory security compliance training for all contact center personnel handling cardholder and personal data.",
                Status = "published",
                CreatedAt = new DateTime(2026, 2, 1, 11, 0, 0, DateTimeKind.Utc),
                Sections = new List<Section>
                {
                    new()
                    {
                        Id = 3, Title = "Section 1: Data Masking & Secure Data Entry", Order = 1,
                        Items = new List<Item>
                        {
                            new() { Id = 31, Title = "Handling Cardholder Data in the CRM",
                                Type = "text", ContentUrl = "",
                                TextContent = "Cardholder Data Security Standards (PCI-DSS)\n\n1. Never record CVV/CVC codes.\n2. Pause screen recording before reading card data into the gateway.\n3. Clean desk policy: no phones, pads, or USB devices on the floor.",
                                Order = 1 },
                            new() { Id = 32, Title = "PCI-DSS Compliance Check",
                                Type = "quiz", ContentUrl = "", TextContent = "", Order = 2,
                                Questions = new List<Question>
                                {
                                    new() { Id = 301, Type = "multiple_choice",
                                        Prompt = "Which of the following elements must NEVER be written down or stored under any circumstances?",
                                        Options = new List<Option>
                                        {
                                            new() { Id = 11, Text = "Customer first name", IsCorrect = false },
                                            new() { Id = 12, Text = "CVV / CVC card security code", IsCorrect = true },
                                            new() { Id = 13, Text = "Ticket ID number", IsCorrect = false }
                                        } }
                                } }
                        }
                    }
                }
            },
            new()
            {
                Id = 103,
                Title = "Omnichannel Live Chat & Tone Calibration",
                Description = "Guidelines for multi-chat concurrency, macro usage, and maintaining empathetic tone across asynchronous channels.",
                Status = "draft",
                CreatedAt = new DateTime(2026, 2, 15, 15, 30, 0, DateTimeKind.Utc),
                Sections = new List<Section>
                {
                    new()
                    {
                        Id = 4, Title = "Section 1: Concurrency Best Practices", Order = 1,
                        Items = new List<Item>
                        {
                            new() { Id = 41, Title = "Balancing 3+ Concurrent Chats",
                                Type = "text", ContentUrl = "",
                                TextContent = "Draft module for managing multiple chat queues efficiently.",
                                Order = 1 }
                        }
                    }
                }
            }
        };
        // Fix up FKs for explicit seeding
        foreach (var c in courses)
            foreach (var s in c.Sections)
            {
                s.CourseId = c.Id;
                foreach (var i in s.Items)
                {
                    i.SectionId = s.Id;
                    foreach (var q in i.Questions)
                    {
                        q.ItemId = i.Id;
                        foreach (var o in q.Options) o.QuestionId = q.Id;
                    }
                }
            }
        db.Courses.AddRange(courses);
        _ = now;
        await db.SaveChangesAsync();
    }
}
