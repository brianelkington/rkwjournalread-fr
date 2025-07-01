# Azure Journal Reader Infrastructure

This Terraform configuration provisions Azure resources for the Journal Reader application.

## Resources Created

- **Resource Group**: `rg-rkwjournalreader`
- **Storage Account**: `sarkwreader1`
  - **Blob Container**: `rkw-text-out`
- **User Assigned Managed Identity**: `id-rkw`
- **Azure Cognitive Services (Computer Vision)**: `ai-rkw-vision`
- **Azure Key Vault**: `kv-rkw`
  - Stores Cognitive Services API keys and endpoint as secrets

## Prerequisites

- [Terraform](https://www.terraform.io/downloads.html) >= 1.0
- Azure subscription and credentials (e.g., via `az login`)

## Usage

1. **Navigate to the `iac/` directory:**
   ```sh
   cd iac
   ```

2. **Set your Azure subscription ID in [`terraform.tfvars`](iac/terraform.tfvars`):**
   ```hcl
   subscription_id = "<your-subscription-id>"
   ```

3. **Initialize Terraform:**
   ```sh
   terraform init
   ```

4. **Review the plan:**
   ```sh
   terraform plan
   ```

5. **Apply the configuration:**
   ```sh
   terraform apply
   ```

## Outputs

- The Key Vault will contain:
  - `API-KEY-1` and `API-KEY-2`: Cognitive Services API keys
  - `ENDPOINT`: Cognitive Services endpoint

## File Structure

- [`main.tf`](iac/main.tf): Resource group, managed identity
- [`storage.tf`](iac/storage.tf): Storage account and blob container
- [`cognitiveservice.tf`](iac/cognitiveservice.tf): Cognitive Services resource
- [`keyvault.tf`](iac/keyvault.tf): Key Vault and secrets
- [`providers.tf`](iac/providers.tf): Azure provider configuration
- [`variables.tf`](iac/variables.tf): Input variables and tags
- [`versions.tf`](iac/versions.tf): Provider version constraints

## Notes

- The storage account uses locally redundant storage (LRS).
- The managed identity can be used for secure access to Azure resources.
- Key Vault access policies are set for both the current user and the managed identity.

---

**See also:**  
- [Terraform Azurerm Provider Documentation](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs)