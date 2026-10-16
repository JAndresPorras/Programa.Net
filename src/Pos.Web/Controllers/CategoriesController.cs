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
            .Include(c => c.ParentCategory)
            .AsNoTracking()
            .ToListAsync();

        // Cada categoria principal seguida de sus subcategorias.
        var ordered = categories
            .Where(c => c.ParentCategoryId is null)
            .OrderBy(c => c.Name)
            .SelectMany(parent => new[] { parent }.Concat(
                categories.Where(c => c.ParentCategoryId == parent.Id).OrderBy(c => c.Name)))
            .ToList();
        return View(ordered);
    }

    /// <summary>Solo pueden ser principales las categorias que no tienen a su vez una principal.</summary>
    private async Task LoadParentsAsync(int? excludeId)
    {
        ViewBag.Parents = await db.Categories
            .Where(c => c.ParentCategoryId == null && c.Id != excludeId)
            .OrderBy(c => c.Name)
            .AsNoTracking()
            .ToListAsync();
    }

    /// <summary>Dos niveles como maximo y sin ciclos.</summary>
    private async Task ValidateParentAsync(Category model)
    {
        if (model.ParentCategoryId is null) return;

        if (model.ParentCategoryId == model.Id)
        {
            ModelState.AddModelError(nameof(model.ParentCategoryId), "Una categoría no puede ser su propia principal.");
            return;
        }

        var parent = await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == model.ParentCategoryId);
        if (parent is null)
        {
            ModelState.AddModelError(nameof(model.ParentCategoryId), "La categoría principal no existe.");
        }
        else if (parent.ParentCategoryId is not null)
        {
            ModelState.AddModelError(nameof(model.ParentCategoryId),
                $"{parent.Name} ya es una subcategoría: elija una categoría principal.");
        }

        if (model.Id > 0 && await db.Categories.AnyAsync(c => c.ParentCategoryId == model.Id))
        {
            ModelState.AddModelError(nameof(model.ParentCategoryId),
                "Esta categoría tiene subcategorías, así que no puede depender de otra.");
        }
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        ViewData["Title"] = "Nueva categoría";
        await LoadParentsAsync(null);
        return View("Form", new Category { Color = "#2563eb", IsActive = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Category model)
    {
        ViewData["Title"] = "Nueva categoría";
        await LoadParentsAsync(null);

        if (await db.Categories.AnyAsync(c => c.Name == model.Name.Trim()))
        {
            ModelState.AddModelError(nameof(model.Name), "Ya existe una categoría con ese nombre.");
        }
        await ValidateParentAsync(model);
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
        await LoadParentsAsync(category.Id);
        return View("Form", category);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Category model)
    {
        var category = await db.Categories.FindAsync(model.Id);
        if (category is null) return NotFound();

        ViewData["Title"] = $"Editar {category.Name}";
        await LoadParentsAsync(category.Id);

        if (await db.Categories.AnyAsync(c => c.Name == model.Name.Trim() && c.Id != model.Id))
        {
            ModelState.AddModelError(nameof(model.Name), "Ya existe otra categoría con ese nombre.");
        }
        await ValidateParentAsync(model);
        if (!ModelState.IsValid) return View("Form", model);

        category.Name = model.Name.Trim();
        category.Description = model.Description;
        category.Color = model.Color;
        category.IsActive = model.IsActive;
        category.ParentCategoryId = model.ParentCategoryId;
        category.InventoryEnabled = model.InventoryEnabled;
        await db.SaveChangesAsync();

        await audit.LogAsync("CategoriaEditada", "Categoria", category.Id.ToString(), category.Name);
        TempData["Success"] = $"La categoría {category.Name} fue actualizada.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var category = await db.Categories
            .Include(c => c.Products)
            .Include(c => c.SubCategories)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (category is null) return NotFound();

        if (category.SubCategories.Count > 0)
        {
            TempData["Error"] =
                $"{category.Name} tiene subcategorías ({string.Join(", ", category.SubCategories.Select(c => c.Name))}). " +
                "Muévalas a otra categoría principal antes de eliminarla.";
            return RedirectToAction(nameof(Index));
        }

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
