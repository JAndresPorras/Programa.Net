using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pos.Web.Data;
using Pos.Web.Models;
using Pos.Web.Security;
using Pos.Web.Services;

namespace Pos.Web.Controllers;

/// <summary>Categorías con las que se agrupan los productos del maestro.</summary>
[HasPermission(Permissions.CategoriesManage)]
public class CategoriesController(ApplicationDbContext db, IAuditService audit) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        ViewData["Title"] = "Categorías";
        var categories = await db.Categories
            .Include(c => c.Products)
            .OrderBy(c => c.Name)
            .AsNoTracking()
            .ToListAsync();
        return View(categories);
    }

    [HttpGet]
    public IActionResult Create()
    {
        ViewData["Title"] = "Nueva categoría";
        return View("Form", new Category { Color = "#2563eb", IsActive = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Category model)
    {
        ViewData["Title"] = "Nueva categoría";

        if (await db.Categories.AnyAsync(c => c.Name == model.Name.Trim()))
        {
            ModelState.AddModelError(nameof(model.Name), "Ya existe una categoría con ese nombre.");
        }
        if (!ModelState.IsValid) return View("Form", model);

        model.Name = model.Name.Trim();
        db.Categories.Add(model);
        await db.SaveChangesAsync();

        await audit.LogAsync("CategoriaCreada", "Categoria", model.Id.ToString(), model.Name);
        TempData["Success"] = $"La categoría {model.Name} fue creada.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var category = await db.Categories.FindAsync(id);
        if (category is null) return NotFound();

        ViewData["Title"] = $"Editar {category.Name}";
        return View("Form", category);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Category model)
    {
        var category = await db.Categories.FindAsync(model.Id);
        if (category is null) return NotFound();

        ViewData["Title"] = $"Editar {category.Name}";

        if (await db.Categories.AnyAsync(c => c.Name == model.Name.Trim() && c.Id != model.Id))
        {
            ModelState.AddModelError(nameof(model.Name), "Ya existe otra categoría con ese nombre.");
        }
        if (!ModelState.IsValid) return View("Form", model);

        category.Name = model.Name.Trim();
        category.Description = model.Description;
        category.Color = model.Color;
        category.IsActive = model.IsActive;
        await db.SaveChangesAsync();

        await audit.LogAsync("CategoriaEditada", "Categoria", category.Id.ToString(), category.Name);
        TempData["Success"] = $"La categoría {category.Name} fue actualizada.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var category = await db.Categories.Include(c => c.Products).FirstOrDefaultAsync(c => c.Id == id);
        if (category is null) return NotFound();

        if (category.Products.Count > 0)
        {
            // No se borra una categoría con productos: se desactiva para no romper el histórico.
            category.IsActive = false;
            await db.SaveChangesAsync();
            TempData["Success"] =
                $"{category.Name} tiene {category.Products.Count} productos, por eso se desactivó en lugar de eliminarse.";
            return RedirectToAction(nameof(Index));
        }

        db.Categories.Remove(category);
        await db.SaveChangesAsync();
        await audit.LogAsync("CategoriaEliminada", "Categoria", id.ToString(), category.Name);
        TempData["Success"] = $"La categoría {category.Name} fue eliminada.";
        return RedirectToAction(nameof(Index));
    }
}
