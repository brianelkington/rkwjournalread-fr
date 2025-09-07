resource "azurerm_resource_group" "rkw" {
  name     = "rg-rkwjournalreader-fr"
  location = var.location

  tags = local.tags
}

resource "azurerm_user_assigned_identity" "rkw" {
  name                = "id-rkw"
  location            = azurerm_resource_group.rkw.location
  resource_group_name = azurerm_resource_group.rkw.name

  tags = local.tags
}

resource "azurerm_log_analytics_workspace" "rkw" {
  name                = "law-rkw"
  location            = azurerm_resource_group.rkw.location
  resource_group_name = azurerm_resource_group.rkw.name

  tags = local.tags
}

