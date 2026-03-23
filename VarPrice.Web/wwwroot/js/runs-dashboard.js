(() => {
    const root = document.getElementById("runsDashboard");
    if (!root || typeof window.kendo === "undefined" || typeof window.jQuery === "undefined") {
        return;
    }

    const $ = window.jQuery;
    const selectedRunLabel = document.getElementById("selectedRunLabel");
    const selectedSnapshotLabel = document.getElementById("selectedSnapshotLabel");
    const productContextLabel = document.getElementById("productContextLabel");

    const snapshotEmptyNoRun = "Select a run to view snapshots";
    const snapshotEmptyNoData = "No snapshots found for selected run";
    const productEmptyNoSnapshot = "Select a snapshot to view products";
    const productEmptyNoData = "No products found for selected snapshot";

    let selectedRunId = null;
    let selectedSnapshotId = null;

    const encode = (value) => window.kendo.htmlEncode(value ?? "");

    const readResultValue = (response, camelCaseKey, pascalCaseKey, fallbackValue) => {
        if (!response || typeof response !== "object") {
            return fallbackValue;
        }

        if (response[camelCaseKey] !== undefined) {
            return response[camelCaseKey];
        }

        if (response[pascalCaseKey] !== undefined) {
            return response[pascalCaseKey];
        }

        return fallbackValue;
    };

    const createDataSource = (url, extraDataFactory, fields, pageSize, sort) => new window.kendo.data.DataSource({
        transport: {
            read: {
                url,
                dataType: "json",
                data: extraDataFactory ?? (() => ({}))
            }
        },
        schema: {
            data: (response) => readResultValue(response, "data", "Data", []),
            total: (response) => readResultValue(response, "total", "Total", 0),
            errors: (response) => readResultValue(response, "errors", "Errors", null),
            model: {
                id: "id",
                fields
            }
        },
        pageSize,
        sort,
        serverPaging: true,
        serverSorting: true,
        serverFiltering: true
    });

    const reloadGrid = (grid) => {
        if (grid.dataSource.page() !== 1) {
            grid.dataSource.page(1);
            return;
        }

        grid.dataSource.read();
    };

    const updateNoRecordsText = (grid, text) => {
        grid.wrapper.find(".k-grid-norecords td").text(text);
    };

    const refreshContextLabels = () => {
        selectedRunLabel.textContent = selectedRunId === null ? "Selected Run: none" : `Selected Run: #${selectedRunId}`;
        selectedSnapshotLabel.textContent = selectedSnapshotId === null
            ? "Selected Snapshot: none"
            : `Selected Snapshot: #${selectedSnapshotId}`;
        productContextLabel.textContent = selectedSnapshotId === null
            ? productEmptyNoSnapshot
            : `Selected Snapshot: #${selectedSnapshotId}`;
    };

    $("#topToolbar").kendoToolBar({
        items: [
            {
                template: "<div class='brand-block'><div class='brand-icon' aria-hidden='true'>VP</div><div class='brand-title'>VarPrice Pro</div></div>",
                overflow: "never"
            },
            {type: "spacer"},
            {
                template: "<div class='top-nav'><span class='top-nav-item active'>Product</span><span class='top-nav-item'>Solutions</span><span class='top-nav-item'>Pricing</span><span class='top-nav-item'>Resources</span><span class='top-nav-item'>Company</span></div>",
                overflow: "never"
            },
            {type: "spacer"},
            {
                type: "button",
                text: "Sign Up",
                attributes: {class: "top-btn top-btn-light"},
                overflow: "never"
            },
            {
                type: "button",
                text: "Start Free Trial",
                attributes: {class: "top-btn top-btn-dark"},
                overflow: "never"
            }
        ]
    });

    $("#workspaceTabs").kendoButtonGroup({
        index: 0,
        selection: "single",
        items: [
            {text: "Dashboard"},
            {text: "Properties"},
            {text: "Analytics"},
            {text: "Finance"},
            {text: "Documents"}
        ]
    });

    $("#ingestVegetablesButton").kendoButton({
        themeColor: "success",
        fillMode: "solid",
        rounded: "full",
        size: "large",
        icon: "arrow-rotate-cw",
        click() {
            document.getElementById("ingestForm")?.submit();
        }
    });
    $("#ingestVegetablesButton").addClass("dashboard-primary-button");

    const runsGrid = $("#runsGrid").kendoGrid({
        dataSource: createDataSource(
            root.dataset.runsUrl,
            null,
            {
                id: {type: "number"},
                startedAtUtc: {type: "date"},
                finishedAtUtc: {type: "date"},
                status: {type: "string"},
                itemsCount: {type: "number"}
            },
            25,
            [{field: "startedAtUtc", dir: "desc"}]
        ),
        selectable: "row",
        sortable: {
            mode: "multiple",
            allowUnsort: true,
            showIndexes: true
        },
        filterable: true,
        columnMenu: true,
        resizable: true,
        scrollable: true,
        noRecords: {
            template: "No crawler runs found"
        },
        pageable: {
            refresh: true,
            pageSizes: [25, 50, 100],
            buttonCount: 5
        },
        toolbar: ["search"],
        search: {
            fields: ["id", "status"]
        },
        columns: [
            {field: "id", title: "Id", width: 90},
            {field: "startedAtUtc", title: "StartedAtUtc", format: "{0:yyyy-MM-dd HH:mm}"},
            {field: "status", title: "Status", minResizableWidth: 120},
            {field: "finishedAtUtc", title: "FinishedAtUtc", format: "{0:yyyy-MM-dd HH:mm}"},
            {field: "itemsCount", title: "ItemsCount", width: 120}
        ],
        change() {
            const row = this.dataItem(this.select());
            selectedRunId = row ? row.id : null;
            selectedSnapshotId = null;
            refreshContextLabels();

            snapshotsGrid.clearSelection();
            productsGrid.clearSelection();
            reloadGrid(snapshotsGrid);
            reloadGrid(productsGrid);
        }
    }).data("kendoGrid");

    const snapshotsGrid = $("#snapshotsGrid").kendoGrid({
        dataSource: createDataSource(
            root.dataset.snapshotsUrl,
            () => ({runId: selectedRunId}),
            {
                id: {type: "number"},
                createdAtUtc: {type: "date"},
                city: {type: "string"},
                price: {type: "number"},
                oldPrice: {type: "number"},
                discountPercent: {type: "number"},
                promoFlag: {type: "boolean"},
                inStock: {type: "boolean"}
            },
            25,
            [{field: "createdAtUtc", dir: "desc"}]
        ),
        selectable: "row",
        sortable: {
            mode: "multiple",
            allowUnsort: true,
            showIndexes: true
        },
        filterable: true,
        columnMenu: true,
        resizable: true,
        scrollable: true,
        noRecords: {
            template: snapshotEmptyNoRun
        },
        pageable: {
            refresh: true,
            pageSizes: [25, 50, 100],
            buttonCount: 5
        },
        toolbar: ["search"],
        search: {
            fields: ["id", "city"]
        },
        columns: [
            {field: "id", title: "Id", width: 90},
            {field: "createdAtUtc", title: "CreatedAtUtc", format: "{0:yyyy-MM-dd HH:mm}"},
            {field: "city", title: "City", minResizableWidth: 140},
            {field: "price", title: "Price", format: "{0:n2}"},
            {field: "oldPrice", title: "OldPrice", format: "{0:n2}"},
            {field: "discountPercent", title: "Discount", format: "{0:n1}"},
            {
                field: "promoFlag",
                title: "Promo",
                width: 90,
                template: (dataItem) => dataItem.promoFlag ? "Yes" : "No"
            },
            {
                field: "inStock",
                title: "InStock",
                width: 100,
                template: (dataItem) => dataItem.inStock ? "Yes" : "No"
            }
        ],
        dataBound(e) {
            updateNoRecordsText(this, selectedRunId === null ? snapshotEmptyNoRun : snapshotEmptyNoData);

            e.sender.tbody.children("tr").each((_, row) => {
                const dataItem = e.sender.dataItem(row);
                if (!dataItem) {
                    return;
                }

                const isPromo = dataItem.promoFlag === true;
                const isStrongDiscount = isPromo
                    && dataItem.discountPercent !== null
                    && dataItem.discountPercent > 25;

                $(row).toggleClass("snapshot-promo", isPromo);
                $(row).toggleClass("snapshot-promo-strong", isStrongDiscount);
            });
        },
        change() {
            const row = this.dataItem(this.select());
            selectedSnapshotId = row ? row.id : null;
            refreshContextLabels();
            reloadGrid(productsGrid);
        }
    }).data("kendoGrid");

    const productsGrid = $("#productsGrid").kendoGrid({
        dataSource: createDataSource(
            root.dataset.productsUrl,
            () => ({snapshotId: selectedSnapshotId}),
            {
                id: {type: "number"},
                name: {type: "string"},
                sku: {type: "string"},
                url: {type: "string"},
                price: {type: "number"},
                unit: {type: "string"},
                updatedAtUtc: {type: "date"}
            },
            50,
            [{field: "updatedAtUtc", dir: "asc"}]
        ),
        sortable: {
            mode: "multiple",
            allowUnsort: true,
            showIndexes: true
        },
        filterable: true,
        columnMenu: true,
        resizable: true,
        scrollable: true,
        noRecords: {
            template: productEmptyNoSnapshot
        },
        pageable: {
            refresh: true,
            pageSizes: [25, 50, 100],
            buttonCount: 5
        },
        toolbar: ["search"],
        search: {
            fields: ["id", "name", "sku", "url", "unit"]
        },
        columns: [
            {field: "id", title: "Id", width: 90},
            {field: "name", title: "Name", minResizableWidth: 220},
            {field: "sku", title: "Sku", minResizableWidth: 130},
            {
                field: "url",
                title: "URL",
                minResizableWidth: 260,
                template(dataItem) {
                    if (!dataItem.url) {
                        return "";
                    }

                    const safeUrl = encode(dataItem.url);
                    return `<a href="${safeUrl}" target="_blank" rel="noopener noreferrer">${safeUrl}</a>`;
                }
            },
            {field: "price", title: "Price", format: "{0:n2}", width: 110},
            {field: "unit", title: "Unit", minResizableWidth: 100},
            {field: "updatedAtUtc", title: "UpdatedAtUtc", format: "{0:yyyy-MM-dd HH:mm}"}
        ],
        dataBound() {
            updateNoRecordsText(this, selectedSnapshotId === null ? productEmptyNoSnapshot : productEmptyNoData);
        }
    }).data("kendoGrid");

    refreshContextLabels();
})();
