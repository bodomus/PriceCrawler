(() => {
    const root = document.getElementById("runsDashboard");
    if (!root || typeof window.kendo === "undefined" || typeof window.jQuery === "undefined") {
        return;
    }

    const $ = window.jQuery;
    const selectedRunLabel = document.getElementById("selectedRunLabel");
    const selectedSnapshotLabel = document.getElementById("selectedSnapshotLabel");
    const productCardContextLabel = document.getElementById("productCardContextLabel");
    const historyContextLabel = document.getElementById("historyContextLabel");
    const priceChartContextLabel = document.getElementById("priceChartContextLabel");
    const productDetailsPanel = document.getElementById("productDetailsPanel");
    const priceChartPanel = document.getElementById("priceChartPanel");
    const analyticsStatus = document.getElementById("analyticsStatus");
    const openSnapshotHistoryButton = document.getElementById("openSnapshotHistoryButton");
    const dashboardSplitterElement = $("#dashboardSplitter");
    const desktopDashboardLayout = window.matchMedia("(min-width: 1181px)");

    const snapshotScopes = {
        none: "none",
        all: "all",
        successful: "successful",
        failed: "failed"
    };

    const snapshotEmptyNoSelection = "Select a run or snapshot group to view snapshots";
    const snapshotEmptyByScope = {
        [snapshotScopes.all]: "No snapshots found for selected run",
        [snapshotScopes.successful]: "No successful snapshots found for selected run",
        [snapshotScopes.failed]: "No failed snapshots found for selected run"
    };
    const historyEmptyNoSelection = "Select a snapshot to view product price history";
    const historyEmptyNoData = "No historical price records found for the selected product";
    const historyEmptyError = "Price history is temporarily unavailable";
    const detailsLoadingText = "Loading product analytics from Postgres...";

    let selectedRunId = null;
    let selectedSnapshotId = null;
    let selectedSnapshotScope = snapshotScopes.none;
    let selectedTreeNode = null;
    let pendingTreeSelectionId = null;
    let shouldExpandRoots = true;
    let currentProductDetails = null;
    let currentProductAnalytics = null;
    let currentProductHistory = [];
    let currentLiveProductResult = null;
    let analysisRequestToken = 0;
    let liveRequestToken = 0;
    let historyHasError = false;
    let historyTotalCount = 0;
    let chartHasError = false;
    let liveProductRequestState = "idle";

    const encode = (value) => window.kendo.htmlEncode(value ?? "");
    const formatDateTime = (value) => value ? window.kendo.toString(value, "yyyy-MM-dd HH:mm") : "Not available";
    const formatPrice = (value) => value === null || value === undefined ? "Not available" : `${window.kendo.toString(value, "n2")} UAH`;
    const formatSignedPrice = (value) => {
        if (value === null || value === undefined) {
            return "No change";
        }

        const sign = value > 0 ? "+" : value < 0 ? "-" : "";
        return `${sign}${window.kendo.toString(Math.abs(value), "n2")} UAH`;
    };
    const formatSignedPercent = (value) => {
        if (value === null || value === undefined) {
            return "Not available";
        }

        const sign = value > 0 ? "+" : value < 0 ? "-" : "";
        return `${sign}${window.kendo.toString(Math.abs(value), "n1")}%`;
    };
    const formatCoverage = (value, total) => {
        if (!total || total <= 0 || value === null || value === undefined) {
            return "Not available";
        }

        return `${window.kendo.toString((value / total) * 100, "n0")}%`;
    };
    const formatBoolLabel = (value, truthyText, falsyText) => {
        if (value === null || value === undefined) {
            return "Not available";
        }

        return value ? truthyText : falsyText;
    };
    const formatRps = (value) => value === null || value === undefined
        ? "Not available"
        : `${window.kendo.toString(value, "n2")} rps`;
    const asDate = (value) => value ? new Date(value) : null;
    const formatText = (value, fallback = "Not available") => {
        if (value === null || value === undefined) {
            return fallback;
        }

        const normalized = `${value}`.trim();
        return normalized.length > 0 ? normalized : fallback;
    };
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
    const requestLayoutResize = () => {
        if (dashboardSplitterElement.length === 0 || typeof window.kendo?.resize !== "function") {
            return;
        }

        window.requestAnimationFrame(() => {
            window.kendo.resize(dashboardSplitterElement.children());
        });
    };
    const resizeProductHistoryGrid = () => {
        const grid = $("#productHistoryGrid").data("kendoGrid");
        if (!grid || typeof window.kendo?.resize !== "function") {
            return;
        }

        window.requestAnimationFrame(() => {
            window.kendo.resize(grid.wrapper);
        });
    };
    const applyViewportDashboardHeight = () => {
        if (dashboardSplitterElement.length === 0) {
            return;
        }

        if (!desktopDashboardLayout.matches) {
            dashboardSplitterElement[0].style.removeProperty("height");
            requestLayoutResize();
            return;
        }

        const layoutBounds = dashboardSplitterElement[0].getBoundingClientRect();
        const viewportPadding = 20;
        const availableHeight = Math.max(540, Math.floor(window.innerHeight - layoutBounds.top - viewportPadding));

        dashboardSplitterElement[0].style.height = `${availableHeight}px`;
        requestLayoutResize();
    };
    const createBadge = (label, value, tone = "neutral") => `
        <div class="analytics-badge analytics-badge-${tone}">
            <span class="analytics-badge-label">${encode(label)}</span>
            <strong>${encode(value)}</strong>
        </div>`;
    const createMetaItem = (label, value) => `
        <div class="product-meta-item">
            <span class="product-meta-label">${encode(label)}</span>
            <strong class="product-meta-value">${encode(value)}</strong>
        </div>`;
    const createEmptyState = (title, message, tone = "neutral") => `
        <div class="analytics-empty-state analytics-empty-state-${tone}">
            <div class="analytics-empty-eyebrow">${tone === "error" ? "Issue" : "Ready State"}</div>
            <h3 class="analytics-empty-title">${encode(title)}</h3>
            <p class="analytics-empty-copy">${encode(message)}</p>
        </div>`;
    const createInsightCard = (eyebrow, title, copy, tone = "neutral") => `
        <article class="analytics-insight-card analytics-insight-card-${tone}">
            <span class="analytics-insight-eyebrow">${encode(eyebrow)}</span>
            <h4 class="analytics-insight-title">${encode(title)}</h4>
            <p class="analytics-insight-copy">${encode(copy)}</p>
        </article>`;
    const createComparisonItem = (label, snapshotValue, liveValue, changed) => `
        <div class="live-compare-item ${changed ? "is-changed" : ""}">
            <span class="live-compare-label">${encode(label)}</span>
            <div class="live-compare-values">
                <span class="live-compare-value">
                    <strong>Snapshot</strong>
                    <span>${encode(snapshotValue)}</span>
                </span>
                <span class="live-compare-value">
                    <strong>Live VARUS</strong>
                    <span>${encode(liveValue)}</span>
                </span>
            </div>
        </div>`;

    const createGridDataSource = (url, extraDataFactory, fields, pageSize, sort, onError) => {
        const dataSource = new window.kendo.data.DataSource({
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

        if (typeof onError === "function") {
            dataSource.bind("error", onError);
        }

        return dataSource;
    };

    const createLocalGridDataSource = (fields, pageSize, sort) => new window.kendo.data.DataSource({
        data: [],
        schema: {
            model: {
                id: "id",
                fields
            }
        },
        pageSize,
        sort,
        serverPaging: false,
        serverSorting: false,
        serverFiltering: false
    });

    const createTreeDataSource = () => new window.kendo.data.TreeListDataSource({
        transport: {
            read: {
                url: root.dataset.runsTreeUrl,
                dataType: "json"
            }
        },
        schema: {
            model: {
                id: "id",
                parentId: "parentId",
                fields: {
                    id: {type: "string"},
                    parentId: {nullable: true},
                    nodeType: {type: "string"},
                    title: {type: "string"},
                    runId: {type: "number", nullable: true},
                    snapshotScope: {type: "string"},
                    startedAtUtc: {type: "date", nullable: true},
                    finishedAtUtc: {type: "date", nullable: true},
                    status: {type: "string"},
                    itemsCount: {type: "number", nullable: true}
                },
                expanded: false
            }
        }
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

    const getSnapshotEmptyText = () => {
        if (selectedRunId === null) {
            return snapshotEmptyNoSelection;
        }

        return snapshotEmptyByScope[selectedSnapshotScope] ?? snapshotEmptyByScope[snapshotScopes.all];
    };
    const getHistoryEmptyText = () => {
        if (selectedSnapshotId === null) {
            return historyEmptyNoSelection;
        }

        return historyHasError ? historyEmptyError : historyEmptyNoData;
    };

    const describeTreeSelection = (node) => {
        if (!node) {
            return "Selected node: none";
        }

        switch (node.nodeType) {
            case "date":
                return `Selected date: ${node.title}`;
            case "run":
                return `Selected run: #${node.runId}`;
            case "successful":
                return `Selected group: Successful snapshots for run #${node.runId}`;
            case "failed":
                return `Selected group: Failed snapshots for run #${node.runId}`;
            default:
                return `Selected node: ${node.title}`;
        }
    };

    const updateAnalyticsLabels = () => {
        if (selectedSnapshotId === null) {
            productCardContextLabel.textContent = "Select a snapshot to inspect a product";
            historyContextLabel.textContent = "Select a snapshot to load price history";
            priceChartContextLabel.textContent = "Real chart will appear for the selected product";
            return;
        }

        if (!currentProductDetails) {
            productCardContextLabel.textContent = `Loading product analytics for snapshot #${selectedSnapshotId}`;
        } else {
            const productName = formatText(currentProductDetails.name, `Product #${currentProductDetails.id}`);
            productCardContextLabel.textContent = `${productName} from snapshot #${currentProductDetails.snapshotId}`;
        }

        if (!currentProductAnalytics && !chartHasError) {
            historyContextLabel.textContent = `Loading history for snapshot #${selectedSnapshotId}`;
            priceChartContextLabel.textContent = `Loading price trajectory for snapshot #${selectedSnapshotId}`;
            return;
        }

        const productName = currentProductDetails
            ? formatText(currentProductDetails.name, `Product #${currentProductDetails.id}`)
            : `Snapshot #${selectedSnapshotId}`;
        const pointsCount = currentProductAnalytics?.historyPointsCount ?? historyTotalCount ?? 0;

        historyContextLabel.textContent = historyHasError
            ? `Price history is unavailable for ${productName}`
            : `${productName} across ${pointsCount} historical records`;
        priceChartContextLabel.textContent = chartHasError
            ? `Chart analytics are unavailable for ${productName}`
            : `Postgres trend chart ready for ${productName}`;
    };

    const refreshContextLabels = () => {
        selectedRunLabel.textContent = describeTreeSelection(selectedTreeNode);
        selectedSnapshotLabel.textContent = selectedSnapshotId === null
            ? "Selected snapshot: none"
            : `Selected snapshot: #${selectedSnapshotId}`;
        updateAnalyticsLabels();
    };

    const setSnapshotSelectionStyles = (cells, isCurrentRow) => {
        cells.each((_, cell) => {
            if (isCurrentRow) {
                cell.style.setProperty("background-color", "var(--selection-bg)", "important");
                cell.style.setProperty("color", "var(--selection-text)", "important");
                cell.style.setProperty("border-top", "1px solid var(--selection-border)", "important");
                cell.style.setProperty("border-bottom", "1px solid var(--selection-border)", "important");
                cell.style.removeProperty("border-left");
                cell.style.removeProperty("border-right");
                cell.style.removeProperty("box-shadow");
                return;
            }

            cell.style.removeProperty("background-color");
            cell.style.removeProperty("color");
            cell.style.removeProperty("border-top");
            cell.style.removeProperty("border-bottom");
            cell.style.removeProperty("border-left");
            cell.style.removeProperty("border-right");
            cell.style.removeProperty("box-shadow");
        });

        if (!isCurrentRow || cells.length === 0) {
            return;
        }

        const firstCell = cells.get(0);
        const lastCell = cells.get(cells.length - 1);

        firstCell?.style.setProperty("border-left", "5px solid var(--accent)", "important");
        firstCell?.style.setProperty("box-shadow", "inset 1px 0 0 var(--selection-border)", "important");
        lastCell?.style.setProperty("border-right", "1px solid var(--selection-border)", "important");
    };
    const syncSnapshotGridSelection = (grid) => {
        if (!grid) {
            return;
        }

        let selectedRow = $();

        grid.tbody.children("tr").each((_, row) => {
            const dataItem = grid.dataItem(row);
            const isCurrentRow = dataItem?.id === selectedSnapshotId;
            const cells = $(row).children("td, .k-table-td");

            $(row).toggleClass("snapshot-current-row", isCurrentRow);
            cells.toggleClass("snapshot-current-cell", isCurrentRow);
            setSnapshotSelectionStyles(cells, isCurrentRow);

            if (isCurrentRow) {
                selectedRow = $(row);
            }
        });

        if (selectedSnapshotId === null || selectedRow.length === 0) {
            grid.clearSelection();
            return;
        }

        grid.select(selectedRow);
    };

    const setAnalyticsStatus = (message, tone = "error") => {
        if (!analyticsStatus) {
            return;
        }

        analyticsStatus.textContent = message;
        analyticsStatus.classList.remove("analytics-status-hidden", "analytics-status-error", "analytics-status-info");
        analyticsStatus.classList.add(`analytics-status-${tone}`);
    };

    const clearAnalyticsStatus = () => {
        if (!analyticsStatus) {
            return;
        }

        analyticsStatus.textContent = "";
        analyticsStatus.classList.add("analytics-status-hidden");
        analyticsStatus.classList.remove("analytics-status-error", "analytics-status-info");
    };

    const renderProductCardEmpty = () => {
        productDetailsPanel.innerHTML = createEmptyState(
            "Product is not selected",
            "Choose a snapshot in the grid above to open the product card."
        );
    };

    const renderProductCardLoading = () => {
        productDetailsPanel.innerHTML = createEmptyState(
            "Loading product card",
            detailsLoadingText
        );
    };

    const renderProductCardError = () => {
        productDetailsPanel.innerHTML = createEmptyState(
            "Product card unavailable",
            "The selected snapshot could not be resolved into a product card.",
            "error"
        );
    };

    const renderChartEmpty = () => {
        destroyPriceChartWidget();
        priceChartPanel.innerHTML = createEmptyState(
            "Chart will appear here",
            "Select a snapshot to render the Postgres price trajectory for the chosen product."
        );
    };

    const renderChartLoading = () => {
        destroyPriceChartWidget();
        priceChartPanel.innerHTML = createEmptyState(
            "Loading price chart",
            detailsLoadingText
        );
    };

    const renderChartError = () => {
        destroyPriceChartWidget();
        priceChartPanel.innerHTML = createEmptyState(
            "Chart analytics unavailable",
            "The price chart could not be built from Postgres history for the selected snapshot.",
            "error"
        );
    };

    const renderChartNoData = () => {
        destroyPriceChartWidget();
        priceChartPanel.innerHTML = createEmptyState(
            "No chart data available",
            "Historical records exist, but they do not contain usable price values for the selected product."
        );
    };

    const activateImageFallback = () => {
        productDetailsPanel.querySelectorAll("[data-product-image]").forEach((image) => {
            image.addEventListener("error", () => {
                const frame = image.closest(".product-media-frame");
                if (!frame) {
                    return;
                }

                frame.innerHTML = `
                    <div class="product-media-placeholder">
                        <span class="product-media-placeholder-icon">VP</span>
                        <span class="product-media-placeholder-text">Image unavailable</span>
                    </div>`;
            }, {once: true});
        });
    };

    const getAntiForgeryToken = () =>
        document.querySelector("#ingestForm input[name='__RequestVerificationToken']")?.value ?? "";

    const valuesDiffer = (left, right) => {
        const normalize = (value) => value === null || value === undefined ? "" : `${value}`.trim().toLowerCase();
        return normalize(left) !== normalize(right);
    };

    const decimalsDiffer = (left, right) => {
        if (left === null || left === undefined) {
            return right !== null && right !== undefined;
        }

        if (right === null || right === undefined) {
            return true;
        }

        return Number(left) !== Number(right);
    };

    const buildLiveComparisonRows = (details, liveCard) => [
        {
            label: "Name",
            snapshotValue: formatText(details.name),
            liveValue: formatText(liveCard.name),
            changed: valuesDiffer(details.name, liveCard.name)
        },
        {
            label: "SKU",
            snapshotValue: formatText(details.sku),
            liveValue: formatText(liveCard.sku),
            changed: valuesDiffer(details.sku, liveCard.sku)
        },
        {
            label: "Current price",
            snapshotValue: formatPrice(details.currentPrice),
            liveValue: formatPrice(liveCard.currentPrice),
            changed: decimalsDiffer(details.currentPrice, liveCard.currentPrice)
        },
        {
            label: "Old price",
            snapshotValue: formatPrice(details.oldPrice),
            liveValue: formatPrice(liveCard.oldPrice),
            changed: decimalsDiffer(details.oldPrice, liveCard.oldPrice)
        },
        {
            label: "Promo",
            snapshotValue: formatBoolLabel(details.promoFlag, "Yes", "No"),
            liveValue: formatBoolLabel(liveCard.promoFlag, "Yes", "No"),
            changed: details.promoFlag !== liveCard.promoFlag
        },
        {
            label: "In stock",
            snapshotValue: formatBoolLabel(details.inStock, "Yes", "No"),
            liveValue: formatBoolLabel(liveCard.inStock, "Yes", "No"),
            changed: details.inStock !== liveCard.inStock
        },
        {
            label: "Unit",
            snapshotValue: formatText(details.unit),
            liveValue: formatText(liveCard.unit),
            changed: valuesDiffer(details.unit, liveCard.unit)
        },
        {
            label: "Slug",
            snapshotValue: formatText(details.slug),
            liveValue: formatText(liveCard.slug),
            changed: valuesDiffer(details.slug, liveCard.slug)
        }
    ];

    const renderLiveRefreshBody = (details) => {
        if (liveProductRequestState === "loading") {
            return `
                <div class="live-refresh-state live-refresh-state-loading">
                    <strong>Refreshing from VARUS...</strong>
                    <p>A manual live request is in flight. Stored Postgres data remains unchanged until you decide the next step.</p>
                </div>`;
        }

        if (liveProductRequestState === "idle" || !currentLiveProductResult) {
            return `
                <div class="live-refresh-state">
                    <strong>Live check is idle</strong>
                    <p>Use the button above to fetch the current VARUS product page for this snapshot and compare it with the stored Postgres values.</p>
                </div>`;
        }

        const result = currentLiveProductResult;
        const liveCard = result.liveCard;
        const tone = result.status === "success"
            ? "success"
            : result.status === "partial"
                ? "accent"
                : "error";
        const comparisonRows = liveCard ? buildLiveComparisonRows(details, liveCard) : [];
        const changedFieldsCount = comparisonRows.filter((row) => row.changed).length;
        const summary = liveCard
            ? changedFieldsCount > 0
                ? `${changedFieldsCount} tracked fields differ from the stored snapshot.`
                : "Tracked snapshot fields currently match the live VARUS response."
            : formatText(result.issue?.message, "VARUS did not return a usable product card.");
        const issueMarkup = result.issue
            ? `
                <div class="live-refresh-issue">
                    <strong>${encode(formatText(result.issue.errorCode, "unknown"))}</strong>
                    <span>${encode(formatText(result.issue.message, "No extra details from extractor."))}</span>
                </div>`
            : "";
        const comparisonMarkup = comparisonRows.length > 0
            ? `
                <div class="live-compare-grid">
                    ${comparisonRows
                .map((row) => createComparisonItem(row.label, row.snapshotValue, row.liveValue, row.changed))
                .join("")}
                </div>`
            : "";

        return `
            <div class="live-refresh-result live-refresh-result-${tone}">
                <div class="live-refresh-result-header">
                    <div>
                        <strong class="live-refresh-result-title">Manual live result: ${encode(formatText(result.status, "unknown"))}</strong>
                        <p class="live-refresh-result-copy">${encode(summary)}</p>
                    </div>
                    <div class="chart-placeholder-metrics">
                        ${createBadge("HTTP", formatText(result.httpStatus), tone)}
                        ${createBadge("Latency", `${formatText(result.latencyMs)} ms`, tone)}
                        ${createBadge("RPS", formatRps(result.approximateRps), "neutral")}
                        ${createBadge("Checked at", formatDateTime(asDate(result.requestedAtUtc)), "muted")}
                    </div>
                </div>
                ${issueMarkup}
                ${comparisonMarkup}
            </div>`;
    };

    const wireLiveRefreshAction = () => {
        const button = document.getElementById("refreshLiveProductButton");
        if (!button) {
            return;
        }

        button.addEventListener("click", () => {
            refreshLiveProduct();
        }, {once: true});
    };

    const resetLiveProductState = () => {
        liveRequestToken += 1;
        currentLiveProductResult = null;
        liveProductRequestState = "idle";
    };

    const renderProductCard = (details) => {
        if (!details) {
            renderProductCardError();
            return;
        }

        const discountValue = details.discountPercent !== null && details.discountPercent !== undefined
            ? `${window.kendo.toString(details.discountPercent, "n1")}%`
            : "No discount";
        const imageMarkup = details.imageUrl
            ? `<img data-product-image src="${encode(details.imageUrl)}" alt="${encode(details.name)}"/>`
            : `
                <div class="product-media-placeholder">
                    <span class="product-media-placeholder-icon">VP</span>
                    <span class="product-media-placeholder-text">Image unavailable</span>
                </div>`;

        productDetailsPanel.innerHTML = `
            <article class="product-card-shell">
                <div class="product-card-hero">
                    <div class="product-media-frame">
                        ${imageMarkup}
                    </div>

                    <div class="product-summary">
                        <div class="product-summary-kicker">Selected product</div>
                        <h3 class="product-summary-title">${encode(formatText(details.name, `Product #${details.id}`))}</h3>
                        <div class="product-summary-badges">
                            ${createBadge("Current", formatPrice(details.currentPrice), "accent")}
                            ${createBadge("Old", formatPrice(details.oldPrice), "muted")}
                            ${createBadge("Discount", discountValue, details.discountPercent ? "success" : "muted")}
                        </div>
                        <div class="product-summary-flags">
                            <span class="product-flag ${details.inStock ? "is-success" : "is-muted"}">
                                ${details.inStock ? "In stock" : "Out of stock"}
                            </span>
                            <span class="product-flag ${details.promoFlag ? "is-success" : "is-muted"}">
                                ${details.promoFlag ? "Promo snapshot" : "Standard snapshot"}
                            </span>
                            <span class="product-flag is-muted">Live Varus not requested</span>
                        </div>
                    </div>
                </div>

                <div class="product-meta-grid">
                    ${createMetaItem("Product ID", formatText(details.id))}
                    ${createMetaItem("External ID", formatText(details.sku))}
                    ${createMetaItem("Unit", formatText(details.unit))}
                    ${createMetaItem("Brand", formatText(details.brand))}
                    ${createMetaItem("Category", formatText(details.category))}
                    ${createMetaItem("Slug", formatText(details.slug))}
                    ${createMetaItem("Captured at", formatDateTime(details.capturedAtUtc))}
                    ${createMetaItem("Updated at", formatDateTime(details.updatedAtUtc))}
                    ${createMetaItem("Run ID", formatText(details.runId))}
                    ${createMetaItem("Source", formatText(details.source, "Postgres"))}
                </div>

                <div class="product-link-panel">
                    <span class="product-meta-label">VARUS URL</span>
                    <a href="${encode(details.url)}" target="_blank" rel="noopener noreferrer">${encode(details.url)}</a>
                </div>

                <section class="product-live-panel">
                    <div class="product-live-header">
                        <div>
                            <span class="product-meta-label">Live VARUS</span>
                            <h4 class="product-live-title">Manual refresh and comparison</h4>
                        </div>
                        <button id="refreshLiveProductButton"
                                class="live-refresh-button"
                                type="button"
                                ${liveProductRequestState === "loading" || !details.url ? "disabled" : ""}>
                            ${liveProductRequestState === "loading" ? "Refreshing..." : "Refresh from VARUS"}
                        </button>
                    </div>
                    ${renderLiveRefreshBody(details)}
                </section>
            </article>`;

        activateImageFallback();
        wireLiveRefreshAction();
    };

    const destroyPriceChartWidget = () => {
        const chart = $("#priceAnalyticsChart").data("kendoChart");
        if (chart) {
            chart.destroy();
        }
    };

    const resolveAnalyticsTone = (value) => {
        if (value === null || value === undefined) {
            return "muted";
        }

        if (value < 0) {
            return "success";
        }

        if (value > 0) {
            return "accent";
        }

        return "neutral";
    };

    const buildTrendSummary = (analytics) => {
        const parts = [];

        if (analytics.changeFromFirstPercent !== null && analytics.changeFromFirstPercent !== undefined) {
            const direction = analytics.changeFromFirstPercent < 0 ? "below" : analytics.changeFromFirstPercent > 0 ? "above" : "aligned with";
            parts.push(`Selected snapshot is ${formatSignedPercent(analytics.changeFromFirstPercent)} ${direction} the first observed price.`);
        }

        if (analytics.changeFromPreviousPercent !== null && analytics.changeFromPreviousPercent !== undefined) {
            const direction = analytics.changeFromPreviousPercent < 0 ? "cheaper" : analytics.changeFromPreviousPercent > 0 ? "higher" : "flat";
            parts.push(`Versus the previous priced snapshot it is ${direction} by ${formatSignedPercent(analytics.changeFromPreviousPercent)}.`);
        }

        if (analytics.historyPointsCount > 0) {
            parts.push(`Promo coverage reached ${formatCoverage(analytics.promoMomentsCount, analytics.historyPointsCount)} and in-stock coverage stayed at ${formatCoverage(analytics.inStockMomentsCount, analytics.historyPointsCount)}.`);
        }

        return parts.join(" ");
    };

    const initializePriceChart = (analytics) => {
        const chartHost = $("#priceAnalyticsChart");
        if (chartHost.length === 0) {
            return;
        }

        const chartPoints = (analytics.points ?? []).map((point) => ({
            snapshotId: point.snapshotId,
            runId: point.runId,
            capturedAtUtc: new Date(point.capturedAtUtc),
            price: point.price,
            oldPrice: point.oldPrice,
            selectedPrice: point.snapshotId === analytics.snapshotId ? point.price : null,
            promoFlag: point.promoFlag,
            inStock: point.inStock,
            source: point.source
        }));

        destroyPriceChartWidget();

        chartHost.kendoChart({
            dataSource: {
                data: chartPoints
            },
            chartArea: {
                background: "transparent"
            },
            legend: {
                position: "bottom",
                labels: {
                    color: "#4c645a"
                }
            },
            transitions: false,
            seriesDefaults: {
                type: "line",
                style: "smooth"
            },
            series: [
                {
                    field: "price",
                    categoryField: "capturedAtUtc",
                    name: "Price",
                    color: "#14824c",
                    width: 3,
                    markers: {
                        visible: true,
                        size: 5,
                        background: "#14824c",
                        border: {
                            color: "#ffffff",
                            width: 2
                        }
                    },
                    missingValues: "gap"
                },
                {
                    field: "oldPrice",
                    categoryField: "capturedAtUtc",
                    name: "Old price",
                    color: "#9ab1a3",
                    width: 2,
                    dashType: "dash",
                    markers: {
                        visible: false
                    },
                    missingValues: "gap"
                },
                {
                    field: "selectedPrice",
                    categoryField: "capturedAtUtc",
                    name: "Selected snapshot",
                    color: "#d47a22",
                    width: 1,
                    markers: {
                        visible: true,
                        size: 8,
                        background: "#d47a22",
                        border: {
                            color: "#ffffff",
                            width: 2
                        }
                    },
                    missingValues: "gap"
                }
            ],
            categoryAxis: {
                baseUnit: chartPoints.length > 14 ? "fit" : "hours",
                labels: {
                    color: "#4c645a",
                    rotation: "auto",
                    format: chartPoints.length > 14 ? "dd MMM" : "dd MMM HH:mm"
                },
                line: {
                    color: "#d7e3dd"
                },
                majorGridLines: {
                    visible: false
                },
                majorTicks: {
                    visible: false
                }
            },
            valueAxis: {
                labels: {
                    color: "#4c645a",
                    format: "{0:n2} UAH"
                },
                line: {
                    visible: false
                },
                majorGridLines: {
                    color: "#e3ece7"
                },
                minorGridLines: {
                    visible: false
                }
            },
            tooltip: {
                visible: true,
                template: "#= series.name #: #= value === null ? 'Not available' : kendo.toString(value, 'n2') + ' UAH' #<br/>#= kendo.toString(category, 'yyyy-MM-dd HH:mm') #"
            }
        });
    };

    const renderChartPanel = () => {
        if (selectedSnapshotId === null) {
            renderChartEmpty();
            return;
        }

        if (chartHasError) {
            renderChartError();
            return;
        }

        if (!currentProductAnalytics) {
            renderChartLoading();
            return;
        }

        const analytics = currentProductAnalytics;
        const hasVisiblePrices = (analytics.points ?? []).some((point) => point.price !== null && point.price !== undefined);
        if (!hasVisiblePrices) {
            renderChartNoData();
            return;
        }

        const trendTone = resolveAnalyticsTone(analytics.changeFromFirstAmount);
        const deltaTone = resolveAnalyticsTone(analytics.changeFromPreviousAmount);
        const trendSummary = buildTrendSummary(analytics);

        priceChartPanel.innerHTML = `
            <div class="price-analytics-shell">
                <div class="price-analytics-copy">
                    <span class="chart-placeholder-kicker">Stage 2 analytics</span>
                    <h3 class="chart-placeholder-title">${encode(formatText(currentProductDetails?.name, "Selected product"))}</h3>
                    <p class="chart-placeholder-text">${encode(trendSummary || "The chart is rendered from Postgres history only. Live VARUS refresh remains a separate manual action for the next stage.")}</p>
                </div>

                <div class="chart-placeholder-metrics">
                    ${createBadge("Selected", formatPrice(analytics.selectedPrice ?? currentProductDetails?.currentPrice), "accent")}
                    ${createBadge("Vs previous", `${formatSignedPrice(analytics.changeFromPreviousAmount)} / ${formatSignedPercent(analytics.changeFromPreviousPercent)}`, deltaTone)}
                    ${createBadge("Vs first", `${formatSignedPrice(analytics.changeFromFirstAmount)} / ${formatSignedPercent(analytics.changeFromFirstPercent)}`, trendTone)}
                    ${createBadge("Range", formatPrice(analytics.priceSpread), "neutral")}
                    ${createBadge("Average", formatPrice(analytics.averagePrice), "muted")}
                    ${createBadge("History points", formatText(analytics.historyPointsCount), "neutral")}
                </div>

                <div class="analytics-insight-grid">
                    ${createInsightCard("Price window", `${formatPrice(analytics.minPrice)} to ${formatPrice(analytics.maxPrice)}`, `Observed from ${formatDateTime(analytics.firstCapturedAtUtc)} to ${formatDateTime(analytics.lastCapturedAtUtc)}.`)}
                    ${createInsightCard("Availability", formatCoverage(analytics.inStockMomentsCount, analytics.historyPointsCount), `${formatText(analytics.inStockMomentsCount)} of ${formatText(analytics.historyPointsCount)} snapshots were in stock.`, "success")}
                    ${createInsightCard("Promo presence", formatCoverage(analytics.promoMomentsCount, analytics.historyPointsCount), `${formatText(analytics.promoMomentsCount)} snapshots carried a promo flag.`, "neutral")}
                </div>

                <div class="price-analytics-chart-frame">
                    <div id="priceAnalyticsChart" class="price-analytics-chart"></div>
                </div>

                <div class="price-analytics-footer">
                    <span>Selected snapshot captured at ${encode(formatDateTime(analytics.selectedCapturedAtUtc))}</span>
                    <span>Latest observed price: ${encode(formatPrice(analytics.latestObservedPrice))}</span>
                </div>
            </div>`;

        initializePriceChart(analytics);
    };

    const refreshLiveProduct = async () => {
        if (selectedSnapshotId === null || !currentProductDetails) {
            return;
        }

        const requestToken = ++liveRequestToken;
        liveProductRequestState = "loading";
        currentLiveProductResult = null;
        renderProductCard(currentProductDetails);

        try {
            const response = await fetch(root.dataset.liveProductUrl, {
                method: "POST",
                headers: {
                    "Accept": "application/json",
                    "Content-Type": "application/x-www-form-urlencoded; charset=UTF-8",
                    "RequestVerificationToken": getAntiForgeryToken()
                },
                body: new URLSearchParams({
                    snapshotId: `${selectedSnapshotId}`
                })
            });

            if (!response.ok) {
                throw new Error(await extractErrorMessage(response));
            }

            const payload = await response.json();
            if (requestToken !== liveRequestToken) {
                return;
            }

            currentLiveProductResult = payload;
            liveProductRequestState = "ready";
            renderProductCard(currentProductDetails);
        } catch (error) {
            if (requestToken !== liveRequestToken) {
                return;
            }

            currentLiveProductResult = {
                snapshotId: selectedSnapshotId,
                requestedAtUtc: new Date().toISOString(),
                requestedUrl: currentProductDetails.url,
                status: "error",
                httpStatus: null,
                latencyMs: 0,
                approximateRps: null,
                liveCard: null,
                issue: {
                    errorCode: "manual_live_request_failed",
                    message: error.message || "Unknown error"
                }
            };
            liveProductRequestState = "failed";
            renderProductCard(currentProductDetails);
        }
    };

    const resetAnalyticsPanels = () => {
        currentProductDetails = null;
        currentProductAnalytics = null;
        currentProductHistory = [];
        historyHasError = false;
        historyTotalCount = 0;
        chartHasError = false;
        resetLiveProductState();
        clearAnalyticsStatus();
        renderProductCardEmpty();
        renderChartEmpty();
        if (typeof productHistoryGrid !== "undefined" && productHistoryGrid?.dataSource) {
            productHistoryGrid.dataSource.data([]);
        }
    };

    const treeIcon = (nodeType) => {
        switch (nodeType) {
            case "date":
                return `
                    <svg viewBox="0 0 24 24" aria-hidden="true">
                        <path d="M16 2v4"></path>
                        <path d="M8 2v4"></path>
                        <path d="M3 10h18"></path>
                        <rect x="3" y="4" width="18" height="17" rx="3"></rect>
                    </svg>`;
            case "run":
                return `
                    <svg viewBox="0 0 24 24" aria-hidden="true">
                        <path d="M6 4h12a2 2 0 0 1 2 2v12a2 2 0 0 1 -2 2H6a2 2 0 0 1 -2 -2V6a2 2 0 0 1 2 -2"></path>
                        <path d="M10 8l6 4l-6 4z"></path>
                    </svg>`;
            case "successful":
                return `
                    <svg viewBox="0 0 24 24" aria-hidden="true">
                        <circle cx="12" cy="12" r="9"></circle>
                        <path d="M8.5 12.5l2.5 2.5l4.5 -5"></path>
                    </svg>`;
            case "failed":
                return `
                    <svg viewBox="0 0 24 24" aria-hidden="true">
                        <circle cx="12" cy="12" r="9"></circle>
                        <path d="M9 9l6 6"></path>
                        <path d="M15 9l-6 6"></path>
                    </svg>`;
            default:
                return `
                    <svg viewBox="0 0 24 24" aria-hidden="true">
                        <circle cx="12" cy="12" r="9"></circle>
                    </svg>`;
        }
    };

    const renderRunStatus = (status) => {
        if (!status) {
            return "";
        }

        return `<span class="tree-status tree-status-${encode(status)}">${encode(status)}</span>`;
    };

    const renderTreeTitle = (dataItem) => `
        <span class="tree-node tree-node-${encode(dataItem.nodeType)}">
            <span class="tree-node-icon">${treeIcon(dataItem.nodeType)}</span>
            <span class="tree-node-text">${encode(dataItem.title)}</span>
        </span>`;

    const hasTreeNodeId = (value) => value !== null && value !== undefined && value !== "";

    const getTreeRowById = (treeList, nodeId) => treeList.tbody.children("tr").filter((_, row) => {
        const dataItem = treeList.dataItem(row);
        return dataItem?.id === nodeId;
    }).first();

    const clearTreeSelection = (treeList) => {
        if (typeof treeList.clearSelection === "function") {
            treeList.clearSelection();
            return;
        }

        treeList.select($());
    };

    const expandTreeAncestors = (treeList, dataItem) => {
        if (!dataItem) {
            return;
        }

        const ancestorIds = [];
        let parentId = dataItem.parentId;

        while (hasTreeNodeId(parentId)) {
            ancestorIds.unshift(parentId);
            parentId = treeList.dataSource.get(parentId)?.parentId ?? null;
        }

        ancestorIds.forEach((ancestorId) => {
            const ancestorRow = getTreeRowById(treeList, ancestorId);
            if (ancestorRow.length > 0) {
                treeList.expand(ancestorRow);
            }
        });
    };

    const applyTreeSelection = (node, options = {}) => {
        const {reloadRelated = true, resetSnapshotSelection = true} = options;

        selectedTreeNode = node ?? null;
        selectedRunId = node?.runId ?? null;
        selectedSnapshotScope = node?.snapshotScope ?? snapshotScopes.none;

        if (resetSnapshotSelection) {
            selectedSnapshotId = null;
            resetAnalyticsPanels();
        }

        refreshContextLabels();

        if (!reloadRelated) {
            return;
        }

        snapshotsGrid.clearSelection();
        reloadGrid(snapshotsGrid);
        reloadGrid(productHistoryGrid);
    };

    const restoreTreeSelection = (treeList) => {
        if (pendingTreeSelectionId === null) {
            return;
        }

        const nodeIdToRestore = pendingTreeSelectionId;
        pendingTreeSelectionId = null;

        const restoredItem = treeList.dataSource.get(nodeIdToRestore);
        if (!restoredItem) {
            clearTreeSelection(treeList);
            applyTreeSelection(null);
            return;
        }

        expandTreeAncestors(treeList, restoredItem);

        const restoredRow = getTreeRowById(treeList, nodeIdToRestore);
        if (restoredRow.length === 0) {
            clearTreeSelection(treeList);
            applyTreeSelection(null);
            return;
        }

        treeList.select(restoredRow);
        applyTreeSelection(treeList.dataItem(restoredRow), {
            reloadRelated: false,
            resetSnapshotSelection: false
        });
    };

    const refreshTreeList = () => {
        const treeList = $("#runsTreeList").data("kendoTreeList");
        if (!treeList) {
            return;
        }

        pendingTreeSelectionId = selectedTreeNode?.id ?? null;
        shouldExpandRoots = true;
        treeList.dataSource.read();
    };

    const extractErrorMessage = async (response) => {
        try {
            const payload = await response.json();
            return payload?.error || response.statusText || "Unknown error";
        } catch {
            return response.statusText || "Unknown error";
        }
    };

    const loadProductAnalysis = async () => {
        const requestToken = ++analysisRequestToken;
        if (selectedSnapshotId === null) {
            resetAnalyticsPanels();
            refreshContextLabels();
            return;
        }

        currentProductDetails = null;
        currentProductAnalytics = null;
        currentProductHistory = [];
        historyHasError = false;
        historyTotalCount = 0;
        chartHasError = false;
        renderProductCardLoading();
        renderChartLoading();
        productHistoryGrid.dataSource.data([]);
        refreshContextLabels();

        const requestUrl = new URL(root.dataset.productAnalysisUrl, window.location.origin);
        requestUrl.searchParams.set("snapshotId", selectedSnapshotId);

        try {
            const response = await fetch(requestUrl.toString(), {
                headers: {
                    "Accept": "application/json"
                }
            });

            if (!response.ok) {
                throw new Error(await extractErrorMessage(response));
            }

            const payload = await response.json();
            if (requestToken !== analysisRequestToken) {
                return;
            }

            currentProductDetails = payload?.productCard ?? null;
            currentProductAnalytics = payload?.analytics ?? {
                snapshotId: selectedSnapshotId,
                historyPointsCount: 0,
                pricePointsCount: 0,
                promoMomentsCount: 0,
                inStockMomentsCount: 0,
                points: []
            };
            currentProductHistory = Array.isArray(payload?.history) ? payload.history : [];
            historyHasError = false;
            historyTotalCount = currentProductHistory.length;
            chartHasError = false;
            productHistoryGrid.dataSource.data(currentProductHistory);
            productHistoryGrid.dataSource.page(1);

            if (!currentProductDetails) {
                renderProductCardError();
                renderChartPanel();
                setAnalyticsStatus("The selected snapshot has no product details in Postgres.");
                refreshContextLabels();
                return;
            }

            renderProductCard(currentProductDetails);
            renderChartPanel();
            refreshContextLabels();
        } catch (error) {
            if (requestToken !== analysisRequestToken) {
                return;
            }

            currentProductDetails = null;
            currentProductAnalytics = null;
            currentProductHistory = [];
            historyHasError = true;
            historyTotalCount = 0;
            chartHasError = true;
            productHistoryGrid.dataSource.data([]);
            renderProductCardError();
            renderChartPanel();
            setAnalyticsStatus(`Product analytics failed to load: ${error.message || "Unknown error"}`);
            refreshContextLabels();
        }
    };

    if (dashboardSplitterElement.length > 0 && desktopDashboardLayout.matches) {
        dashboardSplitterElement.kendoSplitter({
            orientation: "horizontal",
            panes: [
                {
                    size: "32%",
                    min: "320px",
                    collapsible: false,
                    scrollable: false
                },
                {
                    min: "0px",
                    collapsible: false,
                    scrollable: false
                }
            ],
            resize: requestLayoutResize,
            expand: requestLayoutResize,
            collapse: requestLayoutResize
        });
    }

    window.addEventListener("resize", applyViewportDashboardHeight);
    if (typeof desktopDashboardLayout.addEventListener === "function") {
        desktopDashboardLayout.addEventListener("change", applyViewportDashboardHeight);
    } else if (typeof desktopDashboardLayout.addListener === "function") {
        desktopDashboardLayout.addListener(applyViewportDashboardHeight);
    }

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
        size: "medium",
        icon: "arrow-rotate-cw",
        click() {
            document.getElementById("ingestForm")?.submit();
        }
    });
    $("#ingestVegetablesButton").addClass("dashboard-primary-button");
    $("#refreshRunsTreeButton").on("click", refreshTreeList);
    const snapshotHistoryWindow = $("#snapshotHistoryWindow").kendoWindow({
        title: "Price History",
        modal: true,
        visible: false,
        actions: ["Close"],
        width: "min(1100px, 92vw)",
        height: "min(720px, 82vh)",
        resizable: true,
        draggable: true,
        open() {
            this.center();
            resizeProductHistoryGrid();
        },
        activate: resizeProductHistoryGrid,
        resize: resizeProductHistoryGrid,
        refresh: resizeProductHistoryGrid
    }).data("kendoWindow");

    openSnapshotHistoryButton?.addEventListener("click", () => {
        snapshotHistoryWindow.center().open();
    });

    $("#runsTreeList").kendoTreeList({
        dataSource: createTreeDataSource(),
        height: "100%",
        selectable: "row",
        sortable: false,
        resizable: true,
        scrollable: true,
        columns: [
            {
                field: "title",
                title: "Run / Group",
                expandable: true,
                minResizableWidth: 240,
                width: 360,
                template: renderTreeTitle
            },
            {
                field: "status",
                title: "Status",
                width: 120,
                template: (dataItem) => dataItem.nodeType === "run" ? renderRunStatus(dataItem.status) : ""
            },
            {
                field: "startedAtUtc",
                title: "StartedAtUtc",
                width: 165,
                template: (dataItem) => dataItem.nodeType === "run" ? encode(formatDateTime(dataItem.startedAtUtc)) : ""
            },
            {
                field: "finishedAtUtc",
                title: "FinishedAtUtc",
                width: 165,
                template: (dataItem) => dataItem.nodeType === "run" ? encode(formatDateTime(dataItem.finishedAtUtc)) : ""
            },
            {
                field: "itemsCount",
                title: "ItemsCount",
                width: 110,
                attributes: {class: "tree-metric-cell"},
                template: (dataItem) => dataItem.itemsCount ?? ""
            }
        ],
        dataBound(e) {
            if (shouldExpandRoots) {
                e.sender.tbody.children("tr").each((_, row) => {
                    const dataItem = e.sender.dataItem(row);
                    if (dataItem?.parentId == null) {
                        e.sender.expand(row);
                    }
                });
                shouldExpandRoots = false;
            }

            restoreTreeSelection(e.sender);
        },
        change() {
            const row = this.select();
            const item = row.length > 0 ? this.dataItem(row) : null;
            applyTreeSelection(item);
        }
    });

    const snapshotsGrid = $("#snapshotsGrid").kendoGrid({
        dataSource: createGridDataSource(
            root.dataset.snapshotsUrl,
            () => ({
                runId: selectedRunId,
                snapshotScope: selectedSnapshotScope
            }),
            {
                id: {type: "number"},
                createdAtUtc: {type: "date"},
                city: {type: "string"},
                status: {type: "string"},
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
            template: snapshotEmptyNoSelection
        },
        pageable: {
            refresh: true,
            pageSizes: [25, 50, 100],
            buttonCount: 5
        },
        toolbar: ["search"],
        search: {
            fields: ["id", "city", "status"]
        },
        persistSelection: true,
        columns: [
            {field: "id", title: "Id", width: 90},
            {field: "status", title: "Status", width: 120},
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
            updateNoRecordsText(this, getSnapshotEmptyText());

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

            syncSnapshotGridSelection(e.sender);
        },
        change() {
            const row = this.dataItem(this.select());
            selectedSnapshotId = row ? row.id : null;
            currentProductAnalytics = null;
            historyHasError = false;
            historyTotalCount = 0;
            chartHasError = false;
            resetLiveProductState();
            syncSnapshotGridSelection(this);
            clearAnalyticsStatus();
            productHistoryGrid.dataSource.data([]);
            renderChartLoading();
            refreshContextLabels();
            loadProductAnalysis();
        }
    }).data("kendoGrid");

    const productHistoryGrid = $("#productHistoryGrid").kendoGrid({
        dataSource: createLocalGridDataSource(
            {
                id: {type: "number"},
                runId: {type: "number"},
                capturedAtUtc: {type: "date"},
                price: {type: "number"},
                oldPrice: {type: "number"},
                discountPercent: {type: "number"},
                promoFlag: {type: "boolean"},
                inStock: {type: "boolean"},
                source: {type: "string"}
            },
            25,
            [{field: "capturedAtUtc", dir: "desc"}]
        ),
        height: "100%",
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
            template: historyEmptyNoSelection
        },
        pageable: {
            refresh: true,
            pageSizes: [25, 50, 100],
            buttonCount: 5
        },
        toolbar: ["search"],
        search: {
            fields: ["id", "runId", "source"]
        },
        columns: [
            {field: "id", title: "SnapshotId", width: 110},
            {field: "runId", title: "RunId", width: 100},
            {field: "capturedAtUtc", title: "CapturedAtUtc", format: "{0:yyyy-MM-dd HH:mm}"},
            {field: "price", title: "Price", format: "{0:n2}", width: 110},
            {field: "oldPrice", title: "OldPrice", format: "{0:n2}", width: 110},
            {field: "discountPercent", title: "Discount", format: "{0:n1}", width: 110},
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
            },
            {field: "source", title: "Source", minResizableWidth: 140}
        ],
        dataBound(e) {
            historyHasError = false;
            historyTotalCount = e.sender.dataSource.total();
            updateNoRecordsText(this, getHistoryEmptyText());
            refreshContextLabels();
        }
    }).data("kendoGrid");

    resetAnalyticsPanels();
    refreshContextLabels();
    applyViewportDashboardHeight();
})();
