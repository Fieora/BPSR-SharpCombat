var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.BPSR_SharpCombat>("bpsr-sharpcombat");

builder.Build().Run();
