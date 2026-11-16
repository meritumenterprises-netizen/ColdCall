using AdjustCeidgAddress;

if (args.Count() > 0 && args[0].Equals("name", StringComparison.OrdinalIgnoreCase))
{
    return await new CompanyNameAdjuster().RunAsync(args.Skip(1).ToArray());
}
else
if (args.Count() > 0 && args[0].Equals("address", StringComparison.OrdinalIgnoreCase))
{
    return await new AddressAdjuster().RunAsync(args);
}
//if (args.Count() > 0 && args[0].Equals("company", StringComparison.OrdinalIgnoreCase))
//{
//    return await new CompanyAdjuster().RunAsync(args);
//}
else
{
    Console.WriteLine("Invalid argument. Please use 'name', 'address'.");
    return 1;
}
