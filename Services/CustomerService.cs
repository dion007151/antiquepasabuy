using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;
using Microsoft.EntityFrameworkCore;

namespace AntiquePasabuyPOS.Services;

public class CustomerService(AppDbContext db)
{
    public List<Customer> GetAll(bool activeOnly = false, string? businessId = null)
    {
        var targetBusiness = businessId ?? (BusinessProfileService.IsAngelitos ? BusinessProfileService.ANGELITOS_RESTO : BusinessProfileService.ANTIQUE_PASABUY);
        var q = db.Customers.AsNoTracking().Include(c => c.Orders).AsQueryable();

        if (targetBusiness == BusinessProfileService.ANGELITOS_RESTO)
        {
            q = q.Where(c => c.BusinessId == BusinessProfileService.ANGELITOS_RESTO);
        }
        else
        {
            q = q.Where(c => c.BusinessId == BusinessProfileService.ANTIQUE_PASABUY || string.IsNullOrEmpty(c.BusinessId));
        }

        if (activeOnly) q = q.Where(c => c.IsActive);
        return q.OrderBy(c => c.Name).ToList();
    }

    public Customer? GetById(int id)
        => db.Customers.Include(c => c.Orders).FirstOrDefault(c => c.Id == id);

    public Customer Create(string name, string phone = "", string address = "", string notes = "", string? businessId = null)
    {
        var targetBusiness = businessId ?? (BusinessProfileService.IsAngelitos ? BusinessProfileService.ANGELITOS_RESTO : BusinessProfileService.ANTIQUE_PASABUY);
        var code = GetNextCode(targetBusiness);
        var c = new Customer
        {
            BusinessId = targetBusiness,
            Code = code,
            Name = name,
            Phone = phone,
            Address = address,
            Notes = notes
        };
        db.Customers.Add(c);
        db.SaveChanges();
        return c;
    }

    public bool Update(int id, string name, string phone, string address, string notes)
    {
        var c = db.Customers.Find(id);
        if (c is null) return false;
        c.Name = name; c.Phone = phone; c.Address = address; c.Notes = notes;
        db.SaveChanges(); return true;
    }

    public (bool ok, string msg) Delete(int id)
    {
        var c = db.Customers
            .Include(x => x.Orders)
            .FirstOrDefault(x => x.Id == id);

        if (c is null) return (false, "Customer not found.");

        // If the customer has linked orders, detach them first so the FK
        // constraint doesn't block deletion. Orders become "Walk-in" orders.
        if (c.Orders != null && c.Orders.Count > 0)
        {
            foreach (var order in c.Orders)
            {
                order.CustomerId = null;
            }
            db.SaveChanges();
        }

        db.Customers.Remove(c);
        db.SaveChanges();
        return (true, $"Customer '{c.Name}' deleted successfully.");
    }

    private string GetNextCode(string businessId)
    {
        string prefix = (businessId == BusinessProfileService.ANGELITOS_RESTO) ? "RC-" : "C-";
        var last = db.Customers
            .Where(c => c.BusinessId == businessId || (businessId == BusinessProfileService.ANTIQUE_PASABUY && string.IsNullOrEmpty(c.BusinessId)))
            .OrderByDescending(c => c.Id)
            .FirstOrDefault();
        int n = last is null ? 1 : (int.TryParse(last.Code.Replace(prefix, "").Replace("C-", "").Replace("RC-", ""), out int x) ? x + 1 : 1);
        return $"{prefix}{n:D4}";
    }
}
